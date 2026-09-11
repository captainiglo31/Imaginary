using System;
using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Forms;
using Imaginary.Core.Logging;

namespace Imaginary.Desktop.Services;

public class TrayService : ITrayService
{
    private NotifyIcon? _notifyIcon;
    private Window? _mainWindow;
    private ToolStripMenuItem? _hotfolderMenuItem;
    private Action? _toggleHotfolder;
    private Func<bool>? _isHotfolderRunning;
    private Action? _openSettings;
    private Action? _captureScreenshot;
    private bool _isDisposed;

    public void Initialize(Window mainWindow, Action toggleHotfolder, Func<bool> isHotfolderRunning, Action openSettings, Action? captureScreenshot = null)
    {
        _mainWindow = mainWindow ?? throw new ArgumentNullException(nameof(mainWindow));
        _toggleHotfolder = toggleHotfolder;
        _isHotfolderRunning = isHotfolderRunning;
        _openSettings = openSettings;
        _captureScreenshot = captureScreenshot;

        try
        {
            _notifyIcon = new NotifyIcon();

            // Load app icon from pack URI
            try
            {
                var iconUri = new Uri("pack://application:,,,/app.ico");
                var streamResourceInfo = System.Windows.Application.GetResourceStream(iconUri);
                if (streamResourceInfo != null)
                {
                    using var stream = streamResourceInfo.Stream;
                    _notifyIcon.Icon = new Icon(stream);
                }
                else
                {
                    _notifyIcon.Icon = SystemIcons.Application;
                }
            }
            catch
            {
                _notifyIcon.Icon = SystemIcons.Application;
            }

            _notifyIcon.Text = "Imaginary – Modernes Bild-Studio";
            _notifyIcon.Visible = true;

            // Setup Context Menu
            var contextMenu = new ContextMenuStrip();

            var openItem = new ToolStripMenuItem("🖥️ Imaginary öffnen", null, (s, e) => RestoreWindow());
            openItem.Font = new Font(openItem.Font, System.Drawing.FontStyle.Bold);

            _hotfolderMenuItem = new ToolStripMenuItem("📁 Hotfolder: Gestoppt", null, (s, e) =>
            {
                _toggleHotfolder?.Invoke();
                UpdateHotfolderStatus(_isHotfolderRunning?.Invoke() ?? false);
            });

            var settingsItem = new ToolStripMenuItem("⚙️ Einstellungen", null, (s, e) =>
            {
                _openSettings?.Invoke();
                RestoreWindow();
            });

            var exitItem = new ToolStripMenuItem("❌ Imaginary beenden", null, (s, e) =>
            {
                AppLogger.Info("Tray", "Beenden über System-Tray Kontextmenü gewählt.");
                _notifyIcon.Visible = false;
                System.Windows.Application.Current.Shutdown();
            });

            var screenshotItem = new ToolStripMenuItem("📸 Screenshot aufnehmen", null, (s, e) =>
            {
                _captureScreenshot?.Invoke();
            });

            contextMenu.Items.Add(openItem);
            contextMenu.Items.Add(new ToolStripSeparator());
            contextMenu.Items.Add(screenshotItem);
            contextMenu.Items.Add(_hotfolderMenuItem);
            contextMenu.Items.Add(settingsItem);
            contextMenu.Items.Add(new ToolStripSeparator());
            contextMenu.Items.Add(exitItem);

            _notifyIcon.ContextMenuStrip = contextMenu;

            // Double Click or Balloon Click -> Restore Window
            _notifyIcon.DoubleClick += (s, e) => RestoreWindow();
            _notifyIcon.BalloonTipClicked += (s, e) =>
            {
                RestoreWindow();
                var cb = _onBalloonTipClicked;
                _onBalloonTipClicked = null;
                cb?.Invoke();
            };

            UpdateHotfolderStatus(_isHotfolderRunning?.Invoke() ?? false);
        }
        catch (Exception ex)
        {
            AppLogger.Error("Tray", "Fehler beim Initialisieren des Tray-Icons", ex);
        }
    }

    public void UpdateHotfolderStatus(bool isRunning)
    {
        if (_notifyIcon == null || _hotfolderMenuItem == null) return;

        try
        {
            if (isRunning)
            {
                _hotfolderMenuItem.Text = "🟢 Hotfolder: Aktiv (Klicken zum Stoppen)";
                _notifyIcon.Text = "Imaginary – Hotfolder: Aktiv";
            }
            else
            {
                _hotfolderMenuItem.Text = "📁 Hotfolder: Gestoppt (Klicken zum Starten)";
                _notifyIcon.Text = "Imaginary – Modernes Bild-Studio";
            }
        }
        catch { }
    }

    private Action? _onBalloonTipClicked;

    public void ShowNotification(string title, string message, ToolTipIcon icon = ToolTipIcon.Info, int timeoutMs = 3000, Action? onClick = null)
    {
        if (_notifyIcon == null || !_notifyIcon.Visible) return;

        try
        {
            _onBalloonTipClicked = onClick;
            _notifyIcon.ShowBalloonTip(timeoutMs, title, message, icon);
        }
        catch (Exception ex)
        {
            AppLogger.Warn("Tray", "Konnte Balloon-Tip nicht anzeigen", ex);
        }
    }

    public void SetVisible(bool visible)
    {
        if (_notifyIcon != null)
        {
            _notifyIcon.Visible = visible;
        }
    }

    private void RestoreWindow()
    {
        if (_mainWindow == null) return;

        _mainWindow.Dispatcher.Invoke(() =>
        {
            if (!_mainWindow.IsVisible)
            {
                _mainWindow.Show();
            }
            if (_mainWindow.WindowState == WindowState.Minimized)
            {
                _mainWindow.WindowState = WindowState.Normal;
            }
            _mainWindow.Activate();
            _mainWindow.Focus();
        });
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        if (_notifyIcon != null)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.ContextMenuStrip?.Dispose();
            _notifyIcon.Dispose();
            _notifyIcon = null;
        }
    }
}
