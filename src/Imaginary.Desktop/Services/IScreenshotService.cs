using System.Threading.Tasks;

namespace Imaginary.Desktop.Services;

public interface IScreenshotService
{
    /// <summary>
    /// Startet das native Windows Snipping Tool (ms-screenclip: / Win + Shift + S).
    /// </summary>
    void TriggerNativeSnipping();

    /// <summary>
    /// Prüft, ob ein Bild in der Windows-Zwischenablage liegt, speichert es
    /// im lokalen Screenshots-Verzeichnis und gibt den Pfad zurück.
    /// </summary>
    Task<string?> SaveClipboardImageAsync();
}
