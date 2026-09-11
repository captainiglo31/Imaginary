using System;
using System.Windows;

namespace Imaginary.Desktop.Services;

public interface IClipboardMonitorService : IDisposable
{
    event Action<string>? ScreenshotDetected;
    void StartMonitoring(Window window);
    void StopMonitoring();
    void SuppressNextUpdate();
}
