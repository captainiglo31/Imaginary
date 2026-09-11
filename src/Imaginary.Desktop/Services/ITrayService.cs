using System;
using System.Windows;
using System.Windows.Forms;

namespace Imaginary.Desktop.Services;

public interface ITrayService : IDisposable
{
    void Initialize(Window mainWindow, Action toggleHotfolder, Func<bool> isHotfolderRunning, Action openSettings, Action? captureScreenshot = null);
    void UpdateHotfolderStatus(bool isRunning);
    void ShowNotification(string title, string message, ToolTipIcon icon = ToolTipIcon.Info, int timeoutMs = 3000, Action? onClick = null);
    void SetVisible(bool visible);
}
