using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Imaginary.Core.Logging;
using Microsoft.Win32;

namespace Imaginary.Core.Services;

public class StartMenuIntegration : IStartMenuIntegration
{
    private const string AppShortcutName = "Imaginary.lnk";
    private const string AppExeName = "Imaginary.exe";
    private const string AppDescription = "Imaginary – Professionelles Bild-Studio, Konverter & KI-Optimierung";
    private const string AppPathsSubKey = @"Software\Microsoft\Windows\CurrentVersion\App Paths\Imaginary.exe";

    public string GetShortcutPath()
    {
        if (!OperatingSystem.IsWindows()) return string.Empty;
        var programsFolder = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
        return Path.Combine(programsFolder, AppShortcutName);
    }

    public bool IsRegistered()
    {
        if (!OperatingSystem.IsWindows()) return false;

        try
        {
            var shortcutPath = GetShortcutPath();
            if (!File.Exists(shortcutPath))
            {
                return false;
            }

            using var key = Registry.CurrentUser.OpenSubKey(AppPathsSubKey);
            return key != null;
        }
        catch (Exception ex)
        {
            AppLogger.Warn("StartMenu", "Fehler beim Prüfen des Startmenü-Registrierungsstatus", ex);
            return false;
        }
    }

    public bool Register(string? executablePath = null)
    {
        if (!OperatingSystem.IsWindows()) return false;

        try
        {
            var exe = ResolveExecutablePath(executablePath);
            if (string.IsNullOrWhiteSpace(exe) || !File.Exists(exe))
            {
                AppLogger.Warn("StartMenu", $"Ausführbare Datei nicht gefunden: '{exe}'");
                return false;
            }

            var shortcutPath = GetShortcutPath();
            var dir = Path.GetDirectoryName(shortcutPath);
            if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            // 1. Create or update Windows .lnk shortcut
            CreateShellLink(shortcutPath, exe, Path.GetDirectoryName(exe) ?? string.Empty, AppDescription);

            // 2. Register in HKCU App Paths for Win+R and Windows Search
            using (var key = Registry.CurrentUser.CreateSubKey(AppPathsSubKey))
            {
                if (key != null)
                {
                    key.SetValue("", exe, RegistryValueKind.String);
                    key.SetValue("Path", Path.GetDirectoryName(exe) ?? string.Empty, RegistryValueKind.String);
                }
            }

            AppLogger.Info("StartMenu", $"Imaginary erfolgreich im Startmenü & Windows-Suche registriert: '{shortcutPath}' -> '{exe}'");
            return true;
        }
        catch (Exception ex)
        {
            AppLogger.Error("StartMenu", "Fehler beim Registrieren im Startmenü", ex);
            return false;
        }
    }

    public bool Unregister()
    {
        if (!OperatingSystem.IsWindows()) return false;

        try
        {
            var shortcutPath = GetShortcutPath();
            if (File.Exists(shortcutPath))
            {
                File.Delete(shortcutPath);
                AppLogger.Info("StartMenu", $"Startmenü-Verknüpfung gelöscht: '{shortcutPath}'");
            }

            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\App Paths", writable: true);
            key?.DeleteSubKeyTree(AppExeName, throwOnMissingSubKey: false);

            AppLogger.Info("StartMenu", "Imaginary erfolgreich aus Startmenü und Windows-Suche entfernt.");
            return true;
        }
        catch (Exception ex)
        {
            AppLogger.Error("StartMenu", "Fehler beim Entfernen aus dem Startmenü", ex);
            return false;
        }
    }

    public void Synchronize(bool shouldBeEnabled, string? executablePath = null)
    {
        if (!OperatingSystem.IsWindows()) return;

        try
        {
            if (shouldBeEnabled)
            {
                var exe = ResolveExecutablePath(executablePath);
                if (!string.IsNullOrWhiteSpace(exe) && File.Exists(exe))
                {
                    var shortcutPath = GetShortcutPath();
                    // Re-register if missing or target path needs updating
                    if (!File.Exists(shortcutPath) || !IsRegistered())
                    {
                        Register(exe);
                    }
                    else
                    {
                        // Ensure shortcut targets current running executable
                        CreateShellLink(shortcutPath, exe, Path.GetDirectoryName(exe) ?? string.Empty, AppDescription);
                    }
                }
            }
            else
            {
                if (IsRegistered() || File.Exists(GetShortcutPath()))
                {
                    Unregister();
                }
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warn("StartMenu", "Fehler bei Synchronisation des Startmenü-Status", ex);
        }
    }

    private static string? ResolveExecutablePath(string? executablePath)
    {
        if (!string.IsNullOrWhiteSpace(executablePath)) return executablePath;

        if (!string.IsNullOrWhiteSpace(Environment.ProcessPath) &&
            !Environment.ProcessPath.EndsWith("dotnet.exe", StringComparison.OrdinalIgnoreCase) &&
            File.Exists(Environment.ProcessPath))
        {
            return Environment.ProcessPath;
        }

        try
        {
            var mainModule = Process.GetCurrentProcess().MainModule?.FileName;
            if (!string.IsNullOrWhiteSpace(mainModule) &&
                !mainModule.EndsWith("dotnet.exe", StringComparison.OrdinalIgnoreCase) &&
                File.Exists(mainModule))
            {
                return mainModule;
            }
        }
        catch
        {
            // Process inspection may fail in certain restricted contexts
        }

        var baseDir = AppContext.BaseDirectory;
        var directExe = Path.Combine(baseDir, AppExeName);
        if (File.Exists(directExe)) return directExe;

        return null;
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static void CreateShellLink(string shortcutPath, string targetPath, string workingDir, string description)
    {
        try
        {
            // Attempt via native COM IShellLinkW
            var link = (IShellLinkW)new ShellLink();
            link.SetPath(targetPath);
            link.SetWorkingDirectory(workingDir);
            link.SetDescription(description);
            link.SetIconLocation(targetPath, 0);

            var persistFile = (IPersistFile)link;
            persistFile.Save(shortcutPath, true);
            return;
        }
        catch (Exception ex)
        {
            AppLogger.Warn("StartMenu", "IShellLinkW fehlgeschlagen, versuche WScript.Shell Fallback", ex);
        }

        // Fallback via WScript.Shell
        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType != null)
            {
                dynamic shell = Activator.CreateInstance(shellType)!;
                dynamic shortcut = shell.CreateShortcut(shortcutPath);
                shortcut.TargetPath = targetPath;
                shortcut.WorkingDirectory = workingDir;
                shortcut.Description = description;
                shortcut.IconLocation = $"{targetPath},0";
                shortcut.Save();
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error("StartMenu", "Konnte Shell-Verknüpfung nicht erstellen", ex);
            throw;
        }
    }

    [ComImport]
    [Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLink { }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cchMaxPath, IntPtr pfd, uint fFlags);
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cchMaxName);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cchMaxPath);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cchMaxPath);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
        void GetHotkey(out short pwHotkey);
        void SetHotkey(short wHotkey);
        void GetShowCmd(out int piShowCmd);
        void SetShowCmd(int iShowCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cchIconPath, out int piIcon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
        void Resolve(IntPtr hwnd, uint fFlags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("0000010b-0000-0000-C000-000000000046")]
    private interface IPersistFile
    {
        void GetClassID(out Guid pClassID);
        void IsDirty();
        void Load([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, uint dwMode);
        void Save([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, [MarshalAs(UnmanagedType.Bool)] bool fRemember);
        void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string pszFileName);
        void GetCurFile([Out, MarshalAs(UnmanagedType.LPWStr)] string ppszFileName);
    }
}
