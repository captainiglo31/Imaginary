using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using Imaginary.Core.Logging;
using Imaginary.Desktop.Views;
using GdiBitmap = System.Drawing.Bitmap;
using GdiGraphics = System.Drawing.Graphics;
using GdiSize = System.Drawing.Size;
using GdiCopyPixelOperation = System.Drawing.CopyPixelOperation;
using GdiImageFormat = System.Drawing.Imaging.ImageFormat;

namespace Imaginary.Desktop.Services;

public class ScreenshotService : IScreenshotService
{
    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    public async Task<string?> CaptureRegionAsync()
    {
        try
        {
            var mainWindow = Application.Current.MainWindow;
            bool wasVisible = mainWindow != null && mainWindow.IsVisible && mainWindow.WindowState != WindowState.Minimized;

            if (wasVisible && mainWindow != null)
            {
                mainWindow.Opacity = 0;
            }

            // Brief delay to allow Windows Desktop Window Manager (DWM) to render background cleanly
            await Task.Delay(180);

            int vLeft = (int)SystemParameters.VirtualScreenLeft;
            int vTop = (int)SystemParameters.VirtualScreenTop;
            int vWidth = (int)SystemParameters.VirtualScreenWidth;
            int vHeight = (int)SystemParameters.VirtualScreenHeight;

            using var screenBmp = new GdiBitmap(vWidth, vHeight);
            using (var g = GdiGraphics.FromImage(screenBmp))
            {
                g.CopyFromScreen(vLeft, vTop, 0, 0, new GdiSize(vWidth, vHeight), GdiCopyPixelOperation.SourceCopy);
            }

            var wpfSource = ToBitmapSource(screenBmp);

            var overlay = new ScreenCaptureOverlayWindow(screenBmp, wpfSource);
            var dialogResult = overlay.ShowDialog();

            if (wasVisible && mainWindow != null)
            {
                mainWindow.Opacity = 1;
                mainWindow.Activate();
            }

            if (dialogResult != true || overlay.ResultBitmap == null)
            {
                return null;
            }

            using var cropped = overlay.ResultBitmap;

            // Target directory: %LOCALAPPDATA%\Imaginary\Screenshots\
            var screenshotsDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Imaginary",
                "Screenshots");

            if (!Directory.Exists(screenshotsDir))
            {
                Directory.CreateDirectory(screenshotsDir);
            }

            var fileName = $"Screenshot_{DateTime.Now:yyyy-MM-dd_HHmmss}.png";
            var filePath = Path.Combine(screenshotsDir, fileName);

            cropped.Save(filePath, GdiImageFormat.Png);
            AppLogger.Info("Screenshot", $"Screenshot erfolgreich gespeichert: {filePath}");

            // Copy to Windows Clipboard for instant pasting (e.g. into Teams, Slack, Email)
            try
            {
                var croppedWpf = ToBitmapSource(cropped);
                Clipboard.SetImage(croppedWpf);
                AppLogger.Info("Screenshot", "Screenshot in die Windows-Zwischenablage kopiert.");
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Screenshot", "Konnte Screenshot nicht in Zwischenablage ablegen", ex);
            }

            return filePath;
        }
        catch (Exception ex)
        {
            AppLogger.Error("Screenshot", "Fehler bei der Screenshot-Erfassung", ex);

            if (Application.Current.MainWindow != null)
            {
                Application.Current.MainWindow.Opacity = 1;
            }
            return null;
        }
    }

    private static BitmapSource ToBitmapSource(GdiBitmap bmp)
    {
        var hBitmap = bmp.GetHbitmap();
        try
        {
            return Imaging.CreateBitmapSourceFromHBitmap(
                hBitmap,
                IntPtr.Zero,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
        }
        finally
        {
            DeleteObject(hBitmap);
        }
    }
}
