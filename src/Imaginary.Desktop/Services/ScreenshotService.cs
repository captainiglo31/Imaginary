using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using Imaginary.Core.Logging;

namespace Imaginary.Desktop.Services;

public class ScreenshotService : IScreenshotService
{
    public void TriggerNativeSnipping()
    {
        try
        {
            AppLogger.Info("Screenshot", "Starte Windows Snipping Tool (ms-screenclip:)...");
            Process.Start(new ProcessStartInfo("ms-screenclip:") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AppLogger.Error("Screenshot", "Konnte Windows Snipping Tool nicht starten", ex);
        }
    }

    public async Task<string?> SaveClipboardImageAsync()
    {
        try
        {
            BitmapSource? bmp = null;
            if (Application.Current != null && Application.Current.Dispatcher != null)
            {
                bmp = await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    if (Clipboard.ContainsImage())
                    {
                        return Clipboard.GetImage();
                    }
                    return null;
                });
            }
            else
            {
                if (Clipboard.ContainsImage())
                {
                    bmp = Clipboard.GetImage();
                }
            }

            if (bmp == null)
            {
                return null;
            }

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

            using (var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bmp));
                encoder.Save(fileStream);
            }

            AppLogger.Info("Screenshot", $"Screenshot aus Zwischenablage erfolgreich gespeichert: {filePath}");
            return filePath;
        }
        catch (Exception ex)
        {
            AppLogger.Error("Screenshot", "Fehler beim Speichern des Bildes aus der Zwischenablage", ex);
            return null;
        }
    }
}
