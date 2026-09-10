using System.Diagnostics;
using Microsoft.Win32;

namespace Imaginary.Core.Services;

public class ExplorerIntegration : IExplorerIntegration
{
    private const string MenuTitle = "Mit Imaginary konvertieren";
    private const string FileSubKey = @"Software\Classes\*\shell\Imaginary";
    private const string DirSubKey = @"Software\Classes\Directory\shell\Imaginary";

    public bool IsRegistered()
    {
        if (!OperatingSystem.IsWindows()) return false;

        try
        {
            using var fileKey = Registry.CurrentUser.OpenSubKey(FileSubKey);
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

            var commandValue = $"\"{exe}\" \"%1\"";

            // Register for individual files
            using (var fileKey = Registry.CurrentUser.CreateSubKey(FileSubKey))
            {
                if (fileKey != null)
                {
                    fileKey.SetValue("", MenuTitle);
                    fileKey.SetValue("Icon", exe);
                    using var cmdKey = fileKey.CreateSubKey("command");
                    cmdKey.SetValue("", commandValue);
                }
            }

            // Register for directories
            using (var dirKey = Registry.CurrentUser.CreateSubKey(DirSubKey))
            {
                if (dirKey != null)
                {
                    dirKey.SetValue("", MenuTitle);
                    dirKey.SetValue("Icon", exe);
                    using var cmdKey = dirKey.CreateSubKey("command");
                    cmdKey.SetValue("", commandValue);
                }
            }

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
            Registry.CurrentUser.DeleteSubKeyTree(FileSubKey, throwOnMissingSubKey: false);
            Registry.CurrentUser.DeleteSubKeyTree(DirSubKey, throwOnMissingSubKey: false);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
