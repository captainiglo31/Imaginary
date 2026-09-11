using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using Imaginary.Core.Logging;
using Imaginary.Core.Services;

namespace Imaginary.Desktop.Services;

public class ClipboardMonitorService : IClipboardMonitorService
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool AddClipboardFormatListener(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RemoveClipboardFormatListener(IntPtr hwnd);

    private const int WM_CLIPBOARDUPDATE = 0x031D;

    public static ClipboardMonitorService? Current { get; private set; }

    private readonly ISettingsService _settingsService;
    private readonly IScreenshotService _screenshotService;

    private IntPtr _hwnd = IntPtr.Zero;
    private HwndSource? _hwndSource;
    private bool _isMonitoring;
    private bool _suppressNext;
    private DateTime _lastProcessedUtc = DateTime.MinValue;

    public event Action<string>? ScreenshotDetected;

    public ClipboardMonitorService(ISettingsService settingsService, IScreenshotService screenshotService)
    {
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _screenshotService = screenshotService ?? throw new ArgumentNullException(nameof(screenshotService));
        Current = this;
    }

    public void StartMonitoring(Window window)
    {
        if (window == null) throw new ArgumentNullException(nameof(window));
        if (_isMonitoring) return;

        var helper = new WindowInteropHelper(window);
        if (helper.Handle != IntPtr.Zero)
        {
            AttachListener(helper.Handle);
        }
        else
        {
            window.SourceInitialized += (s, e) =>
            {
                var h = new WindowInteropHelper(window).Handle;
                if (h != IntPtr.Zero)
                {
                    AttachListener(h);
                }
            };
        }
    }

    private void AttachListener(IntPtr hwnd)
    {
        if (_isMonitoring || hwnd == IntPtr.Zero) return;

        try
        {
            _hwnd = hwnd;
            _hwndSource = HwndSource.FromHwnd(hwnd);
            _hwndSource?.AddHook(HwndHook);

            bool success = AddClipboardFormatListener(hwnd);
            if (success)
            {
                _isMonitoring = true;
                AppLogger.Info("ClipboardMonitor", "Windows Zwischenablage-Listener (WM_CLIPBOARDUPDATE) erfolgreich registriert.");
            }
            else
            {
                int error = Marshal.GetLastWin32Error();
                AppLogger.Warn("ClipboardMonitor", $"Konnte AddClipboardFormatListener nicht registrieren. Win32-Fehlercode: {error}");
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error("ClipboardMonitor", "Fehler beim Registrieren des Zwischenablage-Listeners", ex);
        }
    }

    public void StopMonitoring()
    {
        if (!_isMonitoring) return;

        try
        {
            if (_hwnd != IntPtr.Zero)
            {
                RemoveClipboardFormatListener(_hwnd);
            }
            _hwndSource?.RemoveHook(HwndHook);
            _hwndSource = null;
            _hwnd = IntPtr.Zero;
            _isMonitoring = false;
            AppLogger.Info("ClipboardMonitor", "Windows Zwischenablage-Listener entfernt.");
        }
        catch (Exception ex)
        {
            AppLogger.Warn("ClipboardMonitor", "Fehler beim Entfernen des Zwischenablage-Listeners", ex);
        }
    }

    public void SuppressNextUpdate()
    {
        _suppressNext = true;
    }

    private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_CLIPBOARDUPDATE)
        {
            OnClipboardUpdate();
        }
        return IntPtr.Zero;
    }

    private void OnClipboardUpdate()
    {
        if (_suppressNext)
        {
            _suppressNext = false;
            return;
        }

        if (!_settingsService.Settings.AutoDetectScreenshots)
        {
            return;
        }

        // Entprellen (Debounce): Windows sendet bei manchen Apps mehrfach Signale innerhalb weniger Millisekunden
        var now = DateTime.UtcNow;
        if ((now - _lastProcessedUtc).TotalMilliseconds < 600)
        {
            return;
        }

        try
        {
            if (Clipboard.ContainsImage())
            {
                _lastProcessedUtc = now;
                _ = HandleClipboardImageAsync();
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warn("ClipboardMonitor", "Fehler beim Prüfen der Zwischenablage auf Bilddaten", ex);
        }
    }

    private async Task HandleClipboardImageAsync()
    {
        try
        {
            // Kleine Verzögerung (100ms), damit die erzeugende App (z.B. Windows Snipping Tool) die Zwischenablage freigibt
            await Task.Delay(100);

            var filePath = await _screenshotService.SaveClipboardImageAsync();
            if (!string.IsNullOrWhiteSpace(filePath) && File.Exists(filePath))
            {
                AppLogger.Info("ClipboardMonitor", $"Neuer Screenshot aus Windows-Zwischenablage erfasst: {filePath}");
                ScreenshotDetected?.Invoke(filePath);
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error("ClipboardMonitor", "Fehler beim automatischen Erfassen des Screenshots", ex);
        }
    }

    public void Dispose()
    {
        StopMonitoring();
        if (ReferenceEquals(Current, this))
        {
            Current = null;
        }
    }
}
