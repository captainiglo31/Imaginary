using System;
using System.Diagnostics;
using System.IO;
using Imaginary.Core.Logging;
using Microsoft.Win32;

namespace Imaginary.Core.Services;

[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public class FileAssociationService : IFileAssociationService
{
    private const string Progid = "Imaginary.Image";
    private const string ProgidDescription = "Imaginary Bilddatei";
    private const string CapabilitiesSubKey = @"Software\Imaginary\Capabilities";
    private const string RegisteredAppsSubKey = @"Software\RegisteredApplications";
    private const string ApplicationsSubKey = @"Software\Classes\Applications\Imaginary.exe";

    public static readonly string[] SupportedExtensions = new[]
    {
        // Standardformate
        ".jpg", ".jpeg", ".png", ".webp", ".bmp", ".tif", ".tiff", ".avif", ".gif", ".heic", ".heif",
        // Kamera-RAW-Formate
        ".cr2", ".cr3", ".nef", ".arw", ".dng", ".orf", ".rw2", ".pef", ".raf"
    };

    public bool IsRegistered()
    {
        if (!OperatingSystem.IsWindows()) return false;

        try
        {
            using var regAppKey = Registry.CurrentUser.OpenSubKey(RegisteredAppsSubKey);
            if (regAppKey?.GetValue("Imaginary") == null) return false;

            using var progidKey = Registry.CurrentUser.OpenSubKey($@"Software\Classes\{Progid}");
            return progidKey != null;
        }
        catch (Exception ex)
        {
            AppLogger.Warn("FileAssoc", "Fehler beim Prüfen der Dateizuordnungs-Registrierung", ex);
            return false;
        }
    }

    public bool Register(string? executablePath = null)
    {
        if (!OperatingSystem.IsWindows()) return false;

        try
        {
            var exe = executablePath;
            if (string.IsNullOrWhiteSpace(exe))
            {
                exe = Process.GetCurrentProcess().MainModule?.FileName;
            }

            if (string.IsNullOrWhiteSpace(exe) || !File.Exists(exe))
            {
                return false;
            }

            // 1. Progid registrieren: HKCU\Software\Classes\Imaginary.Image
            using (var progKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{Progid}"))
            {
                if (progKey != null)
                {
                    progKey.SetValue("", ProgidDescription);
                    using var iconKey = progKey.CreateSubKey("DefaultIcon");
                    iconKey?.SetValue("", $"\"{exe}\",0");

                    // Verben
                    using var openCmd = progKey.CreateSubKey(@"shell\open\command");
                    openCmd?.SetValue("", $"\"{exe}\" \"%1\"");

                    using var editCmd = progKey.CreateSubKey(@"shell\edit\command");
                    editCmd?.SetValue("", $"\"{exe}\" --edit \"%1\"");
                    using var editKey = progKey.CreateSubKey(@"shell\edit");
                    editKey?.SetValue("", "✏️ Mit Imaginary bearbeiten");

                    using var profiCmd = progKey.CreateSubKey(@"shell\profi\command");
                    profiCmd?.SetValue("", $"\"{exe}\" --profi \"%1\"");
                    using var profiKey = progKey.CreateSubKey(@"shell\profi");
                    profiKey?.SetValue("", "🎛️ Im Profi-Modus (RAW) entwickeln");
                }
            }

            // 2. OpenWithProgids für alle Dateiendungen hinterlegen
            foreach (var ext in SupportedExtensions)
            {
                using var owKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{ext}\OpenWithProgids");
                owKey?.SetValue(Progid, string.Empty, RegistryValueKind.String);
            }

            // 3. Applications-Registrierung: HKCU\Software\Classes\Applications\Imaginary.exe
            using (var appKey = Registry.CurrentUser.CreateSubKey(ApplicationsSubKey))
            {
                if (appKey != null)
                {
                    appKey.SetValue("FriendlyAppName", "Imaginary");
                    using var suppKey = appKey.CreateSubKey("SupportedTypes");
                    if (suppKey != null)
                    {
                        foreach (var ext in SupportedExtensions)
                        {
                            suppKey.SetValue(ext, string.Empty, RegistryValueKind.String);
                        }
                    }

                    using var appOpen = appKey.CreateSubKey(@"shell\open\command");
                    appOpen?.SetValue("", $"\"{exe}\" \"%1\"");
                }
            }

            // 4. Capabilities registrieren: HKCU\Software\Imaginary\Capabilities
            using (var capKey = Registry.CurrentUser.CreateSubKey(CapabilitiesSubKey))
            {
                if (capKey != null)
                {
                    capKey.SetValue("ApplicationName", "Imaginary");
                    capKey.SetValue("ApplicationDescription", "Imaginary – Modernes Bild-Studio, Konverter & RAW-Entwickler");

                    using var faKey = capKey.CreateSubKey("FileAssociations");
                    if (faKey != null)
                    {
                        foreach (var ext in SupportedExtensions)
                        {
                            faKey.SetValue(ext, Progid);
                        }
                    }
                }
            }

            // 5. In RegisteredApplications eintragen
            using (var regApps = Registry.CurrentUser.CreateSubKey(RegisteredAppsSubKey))
            {
                regApps?.SetValue("Imaginary", CapabilitiesSubKey, RegistryValueKind.String);
            }

            AppLogger.Info("FileAssoc", $"Imaginary erfolgreich als Standard-App-Fähigkeit registriert ({SupportedExtensions.Length} Formate).");
            return true;
        }
        catch (Exception ex)
        {
            AppLogger.Error("FileAssoc", "Fehler bei der Registrierung der Dateizuordnungen", ex);
            return false;
        }
    }

    public bool Unregister()
    {
        if (!OperatingSystem.IsWindows()) return false;

        try
        {
            // 1. Aus OpenWithProgids austragen
            foreach (var ext in SupportedExtensions)
            {
                try
                {
                    using var owKey = Registry.CurrentUser.OpenSubKey($@"Software\Classes\{ext}\OpenWithProgids", writable: true);
                    owKey?.DeleteValue(Progid, throwOnMissingValue: false);
                }
                catch { }
            }

            // 2. Progid löschen
            Registry.CurrentUser.DeleteSubKeyTree($@"Software\Classes\{Progid}", throwOnMissingSubKey: false);

            // 3. Applications-Eintrag löschen
            Registry.CurrentUser.DeleteSubKeyTree(ApplicationsSubKey, throwOnMissingSubKey: false);

            // 4. Aus RegisteredApplications austragen
            using (var regApps = Registry.CurrentUser.OpenSubKey(RegisteredAppsSubKey, writable: true))
            {
                regApps?.DeleteValue("Imaginary", throwOnMissingValue: false);
            }

            // 5. Capabilities löschen
            Registry.CurrentUser.DeleteSubKeyTree(@"Software\Imaginary\Capabilities", throwOnMissingSubKey: false);

            AppLogger.Info("FileAssoc", "Dateizuordnungen und Standard-App-Registrierung erfolgreich entfernt.");
            return true;
        }
        catch (Exception ex)
        {
            AppLogger.Error("FileAssoc", "Fehler beim Entfernen der Dateizuordnungen", ex);
            return false;
        }
    }

    public void OpenWindowsDefaultAppsSettings()
    {
        if (!OperatingSystem.IsWindows()) return;

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "ms-settings:defaultapps",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            AppLogger.Warn("FileAssoc", "Konnte ms-settings:defaultapps nicht öffnen", ex);
        }
    }
}
