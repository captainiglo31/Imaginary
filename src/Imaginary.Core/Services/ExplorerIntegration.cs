using System.Diagnostics;
using Microsoft.Win32;

namespace Imaginary.Core.Services;

[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public class ExplorerIntegration : IExplorerIntegration
{
    private const string ConvertMenuTitle = "⚡ Mit Imaginary konvertieren";
    private const string EditMenuTitle = "✏️ Mit Imaginary bearbeiten";
    private const string ProfiMenuTitle = "🎛️ Im Profi-Modus (RAW) entwickeln";
    private const string DirMenuTitle = "⚡ Ordner mit Imaginary konvertieren";

    private const string FileConvertSubKey = @"Software\Classes\*\shell\Imaginary";
    private const string FileEditSubKey = @"Software\Classes\*\shell\Imaginary.Edit";
    private const string FileProfiSubKey = @"Software\Classes\*\shell\Imaginary.Profi";
    private const string DirSubKey = @"Software\Classes\Directory\shell\Imaginary";

    public bool IsRegistered()
    {
        if (!OperatingSystem.IsWindows()) return false;

        try
        {
            using var fileKey = Registry.CurrentUser.OpenSubKey(FileConvertSubKey);
            return fileKey != null;
        }
        catch
        {
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

            // 1. "⚡ Mit Imaginary konvertieren" (Dateien)
            RegisterVerb(FileConvertSubKey, ConvertMenuTitle, exe, $"\"{exe}\" \"%1\"");

            // 2. "✏️ Mit Imaginary bearbeiten" (Paint / Quick-Editor)
            RegisterVerb(FileEditSubKey, EditMenuTitle, exe, $"\"{exe}\" --edit \"%1\"");

            // 3. "🎛️ Im Profi-Modus (RAW) entwickeln" (RAW & Color Grading)
            RegisterVerb(FileProfiSubKey, ProfiMenuTitle, exe, $"\"{exe}\" --profi \"%1\"");

            // 4. "⚡ Ordner mit Imaginary konvertieren" (Verzeichnisse)
            RegisterVerb(DirSubKey, DirMenuTitle, exe, $"\"{exe}\" \"%1\"");

            return true;
        }
        catch
        {
            return false;
        }
    }

    public bool Unregister()
    {
        if (!OperatingSystem.IsWindows()) return false;

        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(FileConvertSubKey, throwOnMissingSubKey: false);
            Registry.CurrentUser.DeleteSubKeyTree(FileEditSubKey, throwOnMissingSubKey: false);
            Registry.CurrentUser.DeleteSubKeyTree(FileProfiSubKey, throwOnMissingSubKey: false);
            Registry.CurrentUser.DeleteSubKeyTree(DirSubKey, throwOnMissingSubKey: false);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void RegisterVerb(string subKeyPath, string title, string iconPath, string command)
    {
        using var key = Registry.CurrentUser.CreateSubKey(subKeyPath);
        if (key != null)
        {
            key.SetValue("", title);
            key.SetValue("Icon", iconPath);
            using var cmdKey = key.CreateSubKey("command");
            cmdKey?.SetValue("", command);
        }
    }
}
