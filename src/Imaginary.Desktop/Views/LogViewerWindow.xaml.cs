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

    public LogViewerWindow()
    {
        InitializeComponent();
        GridLogs.ItemsSource = _visibleEntries;

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
        if (!_isInitialized) return;

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

    private void UpdateSummary()
    {
        if (!_isInitialized || TextLogSummary == null) return;

        var logFile = AppLogger.Instance.LogFilePath;
        var fileInfo = !string.IsNullOrEmpty(logFile) ? $" | Datei: {logFile}" : string.Empty;
        TextLogSummary.Text = $"Einträge: {_visibleEntries.Count} sichtbar von {_allEntries.Count} gesamt{fileInfo}";
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
