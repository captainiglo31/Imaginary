using System.Diagnostics;
using System.IO;
using System.Windows;
using Imaginary.Core.Logging;
using Imaginary.Core.Models;
using Imaginary.Core.Services;

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

        if (!string.IsNullOrWhiteSpace(updateInfo.ReleaseNotes))
        {
            TextReleaseNotes.Text = updateInfo.ReleaseNotes;
        }
        else if (!string.IsNullOrWhiteSpace(updateInfo.ReleaseTitle))
        {
            TextReleaseNotes.Text = updateInfo.ReleaseTitle;
        }
        else
        {
            TextReleaseNotes.Text = "Keine Versionshinweise hinterlegt.";
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
