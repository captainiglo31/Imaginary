namespace Imaginary.Core.Services;

public interface IAutostartService
{
    /// <summary>
    /// Prüft, ob ein Autostart-Eintrag für Imaginary in HKCU vorhanden ist.
    /// </summary>
    bool IsAutostartEnabled();

    /// <summary>
    /// Aktiviert den Autostart für den aktuellen Benutzer (HKCU, keine Adminrechte nötig).
    /// </summary>
    bool EnableAutostart(string? executablePath = null, string arguments = "--tray");

    /// <summary>
    /// Entfernt den Autostart-Eintrag für den aktuellen Benutzer rückstandsfrei.
    /// </summary>
    bool DisableAutostart();

    /// <summary>
    /// Gleicht den hinterlegten Autostart-Pfad mit der aktuellen Position der Portable Exe ab (Selbstheilung bei Verschieben).
    /// </summary>
    void SynchronizeAutostart(bool shouldBeEnabled, string? executablePath = null, string arguments = "--tray");
}
