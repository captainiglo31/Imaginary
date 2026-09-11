using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using Imaginary.Core.Logging;
using Microsoft.Win32;

namespace Imaginary.Desktop.Views;

public partial class LogViewerWindow : Window
{
    private readonly ObservableCollection<LogEntry> _visibleEntries = new();
    private readonly List<LogEntry> _allEntries = new();
    private readonly object _filterLock = new();
    private bool _isInitialized;
    private bool _isLiveSession = true;
    private string _currentSessionName = "Aktuelle Sitzung (Live)";

    public LogViewerWindow()
    {
        InitializeComponent();
        GridLogs.ItemsSource = _visibleEntries;

        PopulateLogSessions();

        // Load existing entries
        var recents = AppLogger.Instance.GetRecentEntries();
        _allEntries.AddRange(recents);

        _isInitialized = true;
        ComboLogLevel.SelectedIndex = 0;
        ApplyFilter();

        // Subscribe to live log stream
        AppLogger.Instance.EntryLogged += OnLiveEntryLogged;
        Closing += (s, e) => AppLogger.Instance.EntryLogged -= OnLiveEntryLogged;

        UpdateSummary();
    }

    private void OnLiveEntryLogged(LogEntry entry)
    {
        if (!_isInitialized || !_isLiveSession) return;

        Dispatcher.InvokeAsync(() =>
        {
            _allEntries.Add(entry);
            if (MatchesFilter(entry))
            {
                _visibleEntries.Add(entry);
                if (CheckAutoScroll?.IsChecked == true && _visibleEntries.Count > 0 && GridLogs != null)
                {
                    try { GridLogs.ScrollIntoView(_visibleEntries[^1]); } catch { }
                }
            }
            UpdateSummary();
        });
    }

    private void ApplyFilter()
    {
        if (!_isInitialized || GridLogs == null) return;

        lock (_filterLock)
        {
            _visibleEntries.Clear();
            var filtered = _allEntries.Where(MatchesFilter).ToList();
            foreach (var item in filtered)
            {
                _visibleEntries.Add(item);
            }

            if (CheckAutoScroll?.IsChecked == true && _visibleEntries.Count > 0)
            {
                try { GridLogs.ScrollIntoView(_visibleEntries[^1]); } catch { }
            }
            UpdateSummary();
        }
    }

    private bool MatchesFilter(LogEntry entry)
    {
        // 1. Level filter
        var levelIndex = ComboLogLevel?.SelectedIndex ?? 0;
        bool levelMatch = levelIndex switch
        {
            1 => entry.Level == LogLevel.Error,
            2 => entry.Level == LogLevel.Warning || entry.Level == LogLevel.Error,
            3 => entry.Level != LogLevel.Debug,
            _ => true
        };

        if (!levelMatch) return false;

        // 2. Search text filter
        var search = TextSearch?.Text?.Trim();
        if (!string.IsNullOrEmpty(search))
        {
            bool searchMatch = entry.Message.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                               entry.Source.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                               (entry.ExceptionDetails != null && entry.ExceptionDetails.Contains(search, StringComparison.OrdinalIgnoreCase));
            if (!searchMatch) return false;
        }

        return true;
    }

    private void PopulateLogSessions()
    {
        ComboLogSessions.Items.Clear();

        var liveItem = new ComboBoxItem { Content = "🟢 Aktuelle Sitzung (Live)", Tag = "live" };
        ComboLogSessions.Items.Add(liveItem);

        var logDir = GetLogDirectory();
        if (Directory.Exists(logDir))
        {
            var files = Directory.GetFiles(logDir, "imaginary-*.log")
                                 .Select(f => new FileInfo(f))
                                 .OrderByDescending(f => f.LastWriteTime)
                                 .ToList();

            foreach (var fi in files)
            {
                string label = fi.Name;
                if (fi.LastWriteTime.Date == DateTime.Today)
                    label += " (Heute)";
                else if (fi.LastWriteTime.Date == DateTime.Today.AddDays(-1))
                    label += " (Gestern)";
                else
                    label += $" ({fi.LastWriteTime:dd.MM.yyyy})";

                var fileItem = new ComboBoxItem { Content = $"📄 {label}", Tag = fi.FullName };
                ComboLogSessions.Items.Add(fileItem);
            }
        }

        var browseItem = new ComboBoxItem { Content = "📁 Externe Logdatei öffnen...", Tag = "browse" };
        ComboLogSessions.Items.Add(browseItem);

        ComboLogSessions.SelectedIndex = 0;
    }

    private string GetLogDirectory()
    {
        var logFile = AppLogger.Instance.LogFilePath;
        if (!string.IsNullOrEmpty(logFile))
        {
            var dir = Path.GetDirectoryName(logFile);
            if (!string.IsNullOrEmpty(dir)) return dir;
        }

        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var portableLogs = Path.Combine(baseDir, "logs");
        if (Directory.Exists(portableLogs)) return portableLogs;

        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Imaginary", "logs");
    }

    private void OnLogSessionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isInitialized || ComboLogSessions.SelectedItem is not ComboBoxItem item) return;

        string tag = item.Tag?.ToString() ?? "live";

        if (tag == "live")
        {
            _isLiveSession = true;
            _currentSessionName = "Aktuelle Sitzung (Live)";
            _allEntries.Clear();
            _allEntries.AddRange(AppLogger.Instance.GetRecentEntries());
            ApplyFilter();
        }
        else if (tag == "browse")
        {
            var dlg = new OpenFileDialog
            {
                Title = "Logdatei auswählen",
                Filter = "Logdateien (*.log;*.txt)|*.log;*.txt|Alle Dateien (*.*)|*.*"
            };

            if (dlg.ShowDialog(this) == true)
            {
                LoadHistoricalLog(dlg.FileName, Path.GetFileName(dlg.FileName));
            }
            else
            {
                // Revert to live
                ComboLogSessions.SelectedIndex = 0;
            }
        }
        else
        {
            LoadHistoricalLog(tag, item.Content?.ToString()?.Replace("📄 ", "") ?? Path.GetFileName(tag));
        }
    }

    private void LoadHistoricalLog(string filePath, string displayName)
    {
        _isLiveSession = false;
        _currentSessionName = displayName;
        _allEntries.Clear();

        var parsed = ParseLogFile(filePath);
        _allEntries.AddRange(parsed);
        ApplyFilter();
    }

    private static List<LogEntry> ParseLogFile(string filePath)
    {
        var result = new List<LogEntry>();
        if (!File.Exists(filePath)) return result;

        try
        {
            var lines = File.ReadAllLines(filePath, Encoding.UTF8);
            LogEntry? currentEntry = null;
            var exBuilder = new StringBuilder();
            var logRegex = new System.Text.RegularExpressions.Regex(@"^\[(\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3})\] \[([A-Z ]+)\] \[([^\]]+)\] (.*)$");

            foreach (var line in lines)
            {
                var match = logRegex.Match(line);
                if (match.Success)
                {
                    if (currentEntry != null)
                    {
                        if (exBuilder.Length > 0)
                        {
                            result.Add(currentEntry with { ExceptionDetails = exBuilder.ToString().TrimEnd() });
                            exBuilder.Clear();
                        }
                        else
                        {
                            result.Add(currentEntry);
                        }
                    }

                    if (DateTime.TryParse(match.Groups[1].Value, out var dt))
                    {
                        string levelStr = match.Groups[2].Value.Trim();
                        var level = levelStr switch
                        {
                            "ERROR" => LogLevel.Error,
                            "WARN" => LogLevel.Warning,
                            "DEBUG" => LogLevel.Debug,
                            _ => LogLevel.Information
                        };
                        string source = match.Groups[3].Value;
                        string message = match.Groups[4].Value;

                        currentEntry = new LogEntry(dt, level, source, message);
                    }
                }
                else if (currentEntry != null)
                {
                    exBuilder.AppendLine(line);
                }
            }

            if (currentEntry != null)
            {
                if (exBuilder.Length > 0)
                {
                    result.Add(currentEntry with { ExceptionDetails = exBuilder.ToString().TrimEnd() });
                }
                else
                {
                    result.Add(currentEntry);
                }
            }
        }
        catch { }

        return result;
    }

    private void UpdateSummary()
    {
        if (!_isInitialized || TextLogSummary == null) return;
        TextLogSummary.Text = $"Sitzung: {_currentSessionName} | Einträge: {_visibleEntries.Count} sichtbar von {_allEntries.Count} gesamt";
    }

    private void OnFilterChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isInitialized) return;
        ApplyFilter();
    }

    private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_isInitialized) return;
        ApplyFilter();
    }

    private void OnGridSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DetailsPanel == null || TextDetails == null) return;

        if (GridLogs.SelectedItem is LogEntry entry && !string.IsNullOrEmpty(entry.ExceptionDetails))
        {
            TextDetails.Text = entry.ExceptionDetails;
            DetailsPanel.Visibility = Visibility.Visible;
        }
        else
        {
            DetailsPanel.Visibility = Visibility.Collapsed;
        }
    }

    private void OnCopyClicked(object sender, RoutedEventArgs e)
    {
        var sb = new StringBuilder();
        foreach (var entry in _visibleEntries)
        {
            sb.AppendLine(entry.ToString());
        }
        if (sb.Length > 0)
        {
            Clipboard.SetText(sb.ToString());
            MessageBox.Show(this, $"{_visibleEntries.Count} Einträge in die Zwischenablage kopiert.", "Protokoll kopiert", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void OnCopyDetailsClicked(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(TextDetails?.Text))
        {
            Clipboard.SetText(TextDetails.Text);
            MessageBox.Show(this, "Fehler-Details in die Zwischenablage kopiert.", "Details kopiert", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void OnExportClicked(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog
        {
            Title = "Protokoll exportieren",
            Filter = "Log-Dateien (*.log)|*.log|Textdateien (*.txt)|*.txt|Alle Dateien (*.*)|*.*",
            FileName = $"imaginary-export-{DateTime.Now:yyyyMMdd-HHmmss}.log"
        };

        if (dlg.ShowDialog() == true)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"# Imaginary Protokoll-Export vom {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine();
            foreach (var entry in _visibleEntries)
            {
                sb.AppendLine(entry.ToString());
            }
            File.WriteAllText(dlg.FileName, sb.ToString(), Encoding.UTF8);
            MessageBox.Show(this, $"Protokoll wurde erfolgreich gespeichert nach:\n{dlg.FileName}", "Export erfolgreich", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void OnOpenFolderClicked(object sender, RoutedEventArgs e)
    {
        var logFile = AppLogger.Instance.LogFilePath;
        var dir = !string.IsNullOrEmpty(logFile) && File.Exists(logFile)
            ? Path.GetDirectoryName(logFile)
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Imaginary", "logs");

        if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = dir,
                UseShellExecute = true
            });
        }
        else
        {
            MessageBox.Show(this, "Der Log-Ordner existiert noch nicht auf der Festplatte.", "Ordner nicht gefunden", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnClearClicked(object sender, RoutedEventArgs e)
    {
        AppLogger.Instance.Clear();
        _allEntries.Clear();
        _visibleEntries.Clear();
        if (DetailsPanel != null) DetailsPanel.Visibility = Visibility.Collapsed;
        UpdateSummary();
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
