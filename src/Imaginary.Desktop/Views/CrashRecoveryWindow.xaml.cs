using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using Imaginary.Core.Logging;
using Imaginary.Core.Services;

namespace Imaginary.Desktop.Views;

public partial class CrashRecoveryWindow : Window
{
    private readonly IUpdateService _updateService;
    private readonly Exception? _exception;

    public CrashRecoveryWindow(Exception? exception = null, IUpdateService? updateService = null)
    {
        InitializeComponent();
        _exception = exception;
        _updateService = updateService ?? new UpdateService();

        // 1. Fehler-Details anzeigen
        if (_exception != null)
        {
            ErrorTextBox.Text = $"{_exception.GetType().Name}: {_exception.Message}\n\n{_exception.StackTrace}";
        }
        else
        {
            ErrorTextBox.Text = "Ein wiederholter Startabbruch wurde registriert.";
        }

        // 2. Rollback-Option prüfen
        if (_updateService.CanRollback(out string? previousVer, out _))
        {
            RollbackButton.Content = $"⏪ Auf {previousVer ?? "vorherige Version"} zurück (Rollback)";
            RollbackButton.IsEnabled = true;
        }
        else
        {
            RollbackButton.Content = "⏪ Kein Rollback verfügbar";
            RollbackButton.IsEnabled = false;
            RollbackButton.Opacity = 0.5;
        }
    }

    public bool UserWantsNormalStart { get; private set; }

    private void OnStartAnywayClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            new StartupHealthTracker().Reset();
        }
        catch { }
        UserWantsNormalStart = true;
        DialogResult = true;
        Close();
    }

    private void OnRollbackClicked(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(this,
            "Möchtest du wirklich auf die vorherige Version zurücksetzen?\n\nDie aktuelle Version wird gesichert und die vorherige Version gestartet.",
            "Rollback bestätigen",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            try
            {
                new StartupHealthTracker().Reset();
            }
            catch { }

            bool ok = _updateService.RollbackToPreviousVersion(restart: true);
            if (ok)
            {
                Application.Current.Shutdown();
            }
            else
            {
                MessageBox.Show(this, "Der Rollback konnte nicht durchgeführt werden.", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private async void OnCheckUpdateClicked(object sender, RoutedEventArgs e)
    {
        CheckUpdateButton.IsEnabled = false;
        StatusPanel.Visibility = Visibility.Visible;
        StatusTextBlock.Text = "Prüfe GitHub auf neue Versionen / Hotfixes...";
        DownloadProgressBar.Visibility = Visibility.Collapsed;

        try
        {
            var updateInfo = await _updateService.CheckForUpdateAsync("captainiglo31", "Imaginary");
            if (updateInfo.IsUpdateAvailable && !string.IsNullOrEmpty(updateInfo.DownloadUrl))
            {
                StatusTextBlock.Text = $"Neues Update {updateInfo.LatestVersion} gefunden! Lade herunter...";
                DownloadProgressBar.Visibility = Visibility.Visible;

                var progress = new Progress<double>(pct =>
                {
                    Dispatcher.Invoke(() => DownloadProgressBar.Value = pct * 100);
                });

                string tempFile = await _updateService.DownloadUpdateAsync(updateInfo.DownloadUrl, progress);
                StatusTextBlock.Text = "Download abgeschlossen. Installiere und starte neu...";

                await Task.Delay(800);
                try { new StartupHealthTracker().Reset(); } catch { }
                _updateService.ApplyUpdateAndRestart(tempFile);
                Application.Current.Shutdown();
            }
            else
            {
                StatusTextBlock.Text = "Kein neueres Update auf GitHub gefunden (Installiert ist die neueste Version).";
                CheckUpdateButton.IsEnabled = true;
            }
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = $"Fehler bei der Update-Prüfung: {ex.Message}";
            CheckUpdateButton.IsEnabled = true;
        }
    }

    private void OnOpenLogClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            string? logFile = AppLogger.Instance.LogFilePath;
            if (!string.IsNullOrEmpty(logFile) && File.Exists(logFile))
            {
                Process.Start(new ProcessStartInfo { FileName = logFile, UseShellExecute = true });
            }
            else
            {
                string logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Imaginary", "logs");
                if (Directory.Exists(logDir))
                {
                    Process.Start(new ProcessStartInfo { FileName = logDir, UseShellExecute = true });
                }
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Konnte Logdatei nicht öffnen: " + ex.Message, "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnExitClicked(object sender, RoutedEventArgs e)
    {
        Application.Current.Shutdown();
    }
}
