using System.Diagnostics;
using System.IO;
using Imaginary.Core.Logging;
using Microsoft.Win32;

namespace Imaginary.Core.Services;

public class AutostartService : IAutostartService
{
    private const string RunSubKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "Imaginary";

    public bool IsAutostartEnabled()
    {
        if (!OperatingSystem.IsWindows()) return false;

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunSubKey, writable: false);
            var value = key?.GetValue(AppName) as string;
            return !string.IsNullOrWhiteSpace(value);
        }
        catch (Exception ex)
        {
            AppLogger.Warn("Autostart", "Konnte Autostart-Status nicht lesen", ex);
            return false;
        }
    }

    public bool EnableAutostart(string? executablePath = null, string arguments = "--tray")
    {
        if (!OperatingSystem.IsWindows()) return false;

        try
        {
            var exe = ResolveExecutablePath(executablePath);
            if (string.IsNullOrWhiteSpace(exe) || !File.Exists(exe))
            {
                AppLogger.Warn("Autostart", $"Ausführbare Datei nicht gefunden: '{exe}'");
                return false;
            }

            var commandValue = string.IsNullOrWhiteSpace(arguments)
                ? $"\"{exe}\""
                : $"\"{exe}\" {arguments}";

            using var key = Registry.CurrentUser.OpenSubKey(RunSubKey, writable: true);
            if (key == null)
            {
                AppLogger.Warn("Autostart", "Konnte Registry-Schlüssel HKCU\\Run nicht öffnen");
                return false;
            }

            key.SetValue(AppName, commandValue, RegistryValueKind.String);
            AppLogger.Info("Autostart", $"Autostart erfolgreich aktiviert: {commandValue}");
            return true;
        }
        catch (Exception ex)
        {
            AppLogger.Error("Autostart", "Fehler beim Aktivieren des Autostarts", ex);
            return false;
        }
    }

    public bool DisableAutostart()
    {
        if (!OperatingSystem.IsWindows()) return false;

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunSubKey, writable: true);
            if (key != null && key.GetValue(AppName) != null)
            {
                key.DeleteValue(AppName, throwOnMissingValue: false);
                AppLogger.Info("Autostart", "Autostart erfolgreich deaktiviert.");
            }
            return true;
        }
        catch (Exception ex)
        {
            AppLogger.Error("Autostart", "Fehler beim Deaktivieren des Autostarts", ex);
            return false;
        }
    }

    public void SynchronizeAutostart(bool shouldBeEnabled, string? executablePath = null, string arguments = "--tray")
    {
        if (!OperatingSystem.IsWindows()) return;

        try
        {
            if (shouldBeEnabled)
            {
                var exe = ResolveExecutablePath(executablePath);
                if (!string.IsNullOrWhiteSpace(exe) && File.Exists(exe))
                {
                    var expectedCommand = string.IsNullOrWhiteSpace(arguments)
                        ? $"\"{exe}\""
                        : $"\"{exe}\" {arguments}";

                    using var key = Registry.CurrentUser.OpenSubKey(RunSubKey, writable: true);
                    if (key != null)
                    {
                        var currentVal = key.GetValue(AppName) as string;
                        if (!string.Equals(currentVal, expectedCommand, StringComparison.OrdinalIgnoreCase))
                        {
                            key.SetValue(AppName, expectedCommand, RegistryValueKind.String);
                            AppLogger.Info("Autostart", $"Autostart-Pfad synchronisiert/korrigiert: {expectedCommand}");
                        }
                    }
                }
            }
            else
            {
                // Sicherstellen, dass kein alter Eintrag vorhanden ist
                DisableAutostart();
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warn("Autostart", "Fehler bei der Autostart-Synchronisierung", ex);
        }
    }

    private static string? ResolveExecutablePath(string? path)
    {
        if (!string.IsNullOrWhiteSpace(path)) return path;

        var processPath = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(processPath)) return processPath;

        try
        {
            return Process.GetCurrentProcess().MainModule?.FileName;
        }
        catch
        {
            return null;
        }
    }
}
