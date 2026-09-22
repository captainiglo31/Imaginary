namespace Imaginary.Core.Services;

public interface IFileAssociationService
{
    /// <summary>
    /// Prüft, ob Imaginary in der Windows-Registry als Anwendung und mit Capabilities registriert ist.
    /// </summary>
    bool IsRegistered();

    /// <summary>
    /// Registriert Imaginary in HKCU als Standard-App-Fähigkeit für alle unterstützten Bildformate
    /// und fügt es zu "Öffnen mit..." (OpenWithProgids) hinzu.
    /// </summary>
    bool Register(string? executablePath = null);

    /// <summary>
    /// Entfernt die Windows-Registrierung für Standard-Apps und OpenWithProgids.
    /// </summary>
    bool Unregister();

    /// <summary>
    /// Öffnet die Windows-Einstellungen "Standard-Apps" (ms-settings:defaultapps),
    /// damit der Benutzer Imaginary mit einem Klick als Standard-Bildanzeige festlegen kann.
    /// </summary>
    void OpenWindowsDefaultAppsSettings();
}
