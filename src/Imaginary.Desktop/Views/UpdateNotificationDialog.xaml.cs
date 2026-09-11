using System.Diagnostics;
using System.IO;
using System.Windows;
using Imaginary.Core.Logging;
using Imaginary.Core.Models;
using Imaginary.Core.Services;

using System.Windows.Controls;

namespace Imaginary.Desktop.Views;

public partial class UpdateNotificationDialog : Window
{
    private readonly UpdateInfo _updateInfo;
    private readonly IUpdateService _updateService;
    private readonly ISettingsService _settingsService;
    private CancellationTokenSource? _downloadCts;

    public UpdateNotificationDialog(UpdateInfo updateInfo, IUpdateService updateService, ISettingsService settingsService)
    {
        InitializeComponent();

        _updateInfo = updateInfo;
        _updateService = updateService;
        _settingsService = settingsService;

        var verText = updateInfo.LatestVersion != null ? updateInfo.LatestVersion.ToString() : updateInfo.TagName ?? "Neu";
        TextPrompt.Text = $"Es ist ein Update auf Version {verText} verfügbar. Möchtest du es jetzt installieren?";

        TextCurrentVersion.Text = $"v{updateInfo.CurrentVersion}";
        TextLatestVersion.Text = updateInfo.LatestVersion != null ? $"v{updateInfo.LatestVersion}" : (updateInfo.TagName ?? "Neu");

        PopulateReleaseNotes(updateInfo);
    }

    private void PopulateReleaseNotes(UpdateInfo updateInfo)
    {
        PanelReleaseNotes.Children.Clear();

        var notes = updateInfo.ReleaseNotes;
        if (string.IsNullOrWhiteSpace(notes))
        {
            notes = updateInfo.ReleaseTitle;
        }

        if (string.IsNullOrWhiteSpace(notes))
        {
            AddNoteItem("✨", "Allgemeine Leistungs- und Stabilitätsoptimierungen");
            AddNoteItem("🐛", "Fehlerbehebungen und Verbesserungen der Benutzeroberfläche");
            return;
        }

        var lines = notes.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        bool addedAny = false;

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line)) continue;

            // Skip GitHub comparison link noise
            if (line.StartsWith("**Full Changelog**", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("Full Changelog:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // Headers like "## What's Changed"
            if (line.StartsWith('#'))
            {
                var headerText = line.TrimStart('#', ' ').Trim();
                if (headerText.Equals("What's Changed", StringComparison.OrdinalIgnoreCase))
                {
                    headerText = "Was ist neu:";
                }
                var tbHeader = new TextBlock
                {
                    Text = headerText,
                    FontWeight = FontWeights.Bold,
                    FontSize = 12,
                    Margin = new Thickness(0, addedAny ? 10 : 0, 0, 4)
                };
                tbHeader.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
                PanelReleaseNotes.Children.Add(tbHeader);
                addedAny = true;
                continue;
            }

            // Bullet items
            if (line.StartsWith('*') || line.StartsWith('-'))
            {
                var content = line.Substring(1).Trim();

                // Remove "... by @user in https://github.com/..."
                var inIndex = content.LastIndexOf(" in https://", StringComparison.OrdinalIgnoreCase);
                if (inIndex > 0)
                {
                    content = content.Substring(0, inIndex).Trim();
                }

                var icon = "•";
                if (content.Contains("fix", StringComparison.OrdinalIgnoreCase) ||
                    content.Contains("bug", StringComparison.OrdinalIgnoreCase) ||
                    content.Contains("fehler", StringComparison.OrdinalIgnoreCase) ||
                    content.Contains("behob", StringComparison.OrdinalIgnoreCase))
                {
                    icon = "🐛";
                }
                else if (content.Contains("add", StringComparison.OrdinalIgnoreCase) ||
                         content.Contains("neu", StringComparison.OrdinalIgnoreCase) ||
                         content.Contains("feature", StringComparison.OrdinalIgnoreCase) ||
                         content.Contains("splash", StringComparison.OrdinalIgnoreCase))
                {
                    icon = "✨";
                }
                else if (content.Contains("perf", StringComparison.OrdinalIgnoreCase) ||
                         content.Contains("speed", StringComparison.OrdinalIgnoreCase) ||
                         content.Contains("optim", StringComparison.OrdinalIgnoreCase))
                {
                    icon = "⚡";
                }

                AddNoteItem(icon, content);
                addedAny = true;
                continue;
            }

            // Regular paragraph line
            AddNoteItem("•", line);
            addedAny = true;
        }

        if (!addedAny)
        {
            AddNoteItem("✨", "Allgemeine Verbesserungen und Stabilitätsoptimierungen");
        }
    }

    private void AddNoteItem(string icon, string text)
    {
        var grid = new Grid { Margin = new Thickness(0, 3, 0, 4) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var tbIcon = new TextBlock
        {
            Text = icon,
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 1, 0, 0)
        };

        var tbText = new TextBlock
        {
            FontSize = 12.5,
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 19,
            VerticalAlignment = VerticalAlignment.Center
        };
        tbText.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");

        AppendFormattedMarkdown(tbText, text);

        Grid.SetColumn(tbIcon, 0);
        Grid.SetColumn(tbText, 1);

        grid.Children.Add(tbIcon);
        grid.Children.Add(tbText);

        PanelReleaseNotes.Children.Add(grid);
    }

    private static void AppendFormattedMarkdown(TextBlock tb, string raw)
    {
        var parts = raw.Split("**");
        for (int i = 0; i < parts.Length; i++)
        {
            if (string.IsNullOrEmpty(parts[i])) continue;

            bool isBold = (i % 2 == 1);
            var run = new System.Windows.Documents.Run(parts[i])
            {
                FontWeight = isBold ? FontWeights.Bold : FontWeights.Normal
            };
            tb.Inlines.Add(run);
        }
    }

    private void OnOpenGithubReleaseClicked(object sender, RoutedEventArgs e)
    {
        var url = !string.IsNullOrWhiteSpace(_updateInfo.HtmlUrl)
            ? _updateInfo.HtmlUrl
            : "https://github.com/captainiglo31/Imaginary/releases";

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            AppLogger.Error("Update", "Konnte Browser für Release-Notes nicht öffnen", ex);
        }
    }

    private void OnSkipVersionClicked(object sender, RoutedEventArgs e)
    {
        if (_updateInfo.LatestVersion != null)
        {
            _settingsService.Settings.SkippedVersion = _updateInfo.LatestVersion.ToString();
            _settingsService.Save();
            AppLogger.Info("Update", $"Version {_updateInfo.LatestVersion} wurde vom Nutzer übersprungen.");
        }
        Close();
    }

    private void OnLaterClicked(object sender, RoutedEventArgs e)
    {
        _downloadCts?.Cancel();
        Close();
    }

    private async void OnInstallClicked(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_updateInfo.DownloadUrl))
        {
            // Falls kein direkter Asset-Downloadlink vorliegt, Browser zum Release öffnen
            if (!string.IsNullOrWhiteSpace(_updateInfo.HtmlUrl))
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = _updateInfo.HtmlUrl,
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    AppLogger.Error("Update", "Konnte Browser nicht öffnen", ex);
                }
            }
            else
            {
                MessageBox.Show(this, "Kein Download-Link für diese Version gefunden.", "Update", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            Close();
            return;
        }

        ButtonInstall.IsEnabled = false;
        ButtonLater.IsEnabled = false;
        PanelProgress.Visibility = Visibility.Visible;

        _downloadCts = new CancellationTokenSource();

        try
        {
            AppLogger.Info("Update", $"Download gestartet: {_updateInfo.DownloadUrl}");
            TextProgressStatus.Text = "Lade Update herunter...";

            var progress = new Progress<double>(percent =>
            {
                var val = Math.Clamp(percent * 100.0, 0, 100.0);
                ProgressDownload.Value = val;
                TextProgressPercentage.Text = $"{val:F0}%";
                if (_updateInfo.FileSizeBytes > 0)
                {
                    var loadedMb = (percent * _updateInfo.FileSizeBytes) / (1024.0 * 1024.0);
                    var totalMb = _updateInfo.FileSizeBytes / (1024.0 * 1024.0);
                    TextProgressStatus.Text = $"Lade herunter... {loadedMb:F1} MB von {totalMb:F1} MB";
                }
            });

            var downloadedFile = await _updateService.DownloadUpdateAsync(_updateInfo.DownloadUrl, progress, _downloadCts.Token);

            TextProgressStatus.Text = "Download abgeschlossen. Imaginary wird neu gestartet...";
            ProgressDownload.Value = 100;
            TextProgressPercentage.Text = "100%";
            AppLogger.Info("Update", $"Download erfolgreich: {downloadedFile}. Führe Update aus und starte neu.");

            await Task.Delay(500);

            var success = _updateService.ApplyUpdateAndRestart(downloadedFile);
            if (success)
            {
                Application.Current.Shutdown();
            }
            else
            {
                throw new InvalidOperationException("Konnte Update-Prozess nicht starten.");
            }
        }
        catch (OperationCanceledException)
        {
            PanelProgress.Visibility = Visibility.Collapsed;
            ButtonInstall.IsEnabled = true;
            ButtonLater.IsEnabled = true;
        }
        catch (Exception ex)
        {
            AppLogger.Error("Update", "Fehler beim Herunterladen oder Installieren des Updates", ex);
            PanelProgress.Visibility = Visibility.Collapsed;
            ButtonInstall.IsEnabled = true;
            ButtonLater.IsEnabled = true;

            MessageBox.Show(this,
                $"Das Update konnte nicht automatisch installiert werden:\n\n{ex.Message}\n\nDu kannst die Version manuell von GitHub herunterladen.",
                "Update-Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _downloadCts?.Cancel();
        _downloadCts?.Dispose();
        base.OnClosed(e);
    }
}
