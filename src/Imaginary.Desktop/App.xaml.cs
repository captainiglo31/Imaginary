using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Imaginary.Core.Logging;
using Imaginary.Desktop.Services;

namespace Imaginary.Desktop;

public partial class App : Application
{
    public static string[] StartupArgs { get; private set; } = Array.Empty<string>();
    public static event Action<string[]>? FilesReceivedViaIpc;

    protected override void OnStartup(StartupEventArgs e)
    {
        // Global Exception Handlers for robust crash diagnosis
        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
        {
            if (args.ExceptionObject is Exception ex)
            {
                AppLogger.Error("AppDomain", "Unbehandelte Ausnahme in Anwendungsdomäne", ex);
            }
        };

        DispatcherUnhandledException += (s, args) =>
        {
            AppLogger.Error("Dispatcher", "Unbehandelte UI-Dispatcher-Ausnahme: " + args.Exception.Message, args.Exception);
            // Don't swallow fatal exceptions, but ensure they are logged
        };

        TaskScheduler.UnobservedTaskException += (s, args) =>
        {
            AppLogger.Error("TaskScheduler", "Unbeobachtete Task-Ausnahme: " + args.Exception.Message, args.Exception);
            args.SetObserved();
        };

        StartupArgs = e.Args;

        AppLogger.Info("App", $"=== Imaginary gestartet (PID: {Environment.ProcessId}) ===");
        AppLogger.Info("App", $"OS: {Environment.OSVersion}, .NET: {Environment.Version}, 64-Bit: {Environment.Is64BitProcess}");
        AppLogger.Info("App", $"Logdatei: {AppLogger.Instance.LogFilePath ?? "Im Arbeitsspeicher"}");
        if (e.Args.Length > 0)
        {
            AppLogger.Info("App", $"Startargumente ({e.Args.Length}): {string.Join(", ", e.Args)}");
        }

        bool isFirstInstance = SingleInstanceService.TryRegisterSingleInstance(e.Args, files =>
        {
            Current.Dispatcher.Invoke(() =>
            {
                AppLogger.Info("SingleInstance", $"Dateien über IPC empfangen ({files.Length} Stück): {string.Join(", ", files)}");
                FilesReceivedViaIpc?.Invoke(files);
                if (Current.MainWindow != null)
                {
                    var helper = new WindowInteropHelper(Current.MainWindow);
                    SingleInstanceService.ActivateWindow(helper.Handle);
                }
            });
        });

        if (!isFirstInstance)
        {
            AppLogger.Info("App", "Folgeinstanz erkannt – Argumente an primäre Instanz übermittelt. Beende Folgeprozess.");
            Shutdown();
            return;
        }

        // Ggf. verbliebene .old-Dateien aus vorherigen Updates im Hintergrund bereinigen
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(2000);
                var exePath = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(exePath))
                {
                    var dir = Path.GetDirectoryName(exePath);
                    if (dir != null && Directory.Exists(dir))
                    {
                        foreach (var oldFile in Directory.EnumerateFiles(dir, "*.old"))
                        {
                            try { File.Delete(oldFile); } catch { }
                        }
                    }
                }
            }
            catch { }
        });

        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        AppLogger.Info("App", $"=== Imaginary wird beendet (ExitCode: {e.ApplicationExitCode}) ===");
        SingleInstanceService.Cleanup();
        base.OnExit(e);
    }

    public static void SetTheme(bool isDark)
    {
        var themeName = isDark ? "DarkTheme" : "LightTheme";
        AppLogger.Debug("Theme", $"Wechsle Design zu: {themeName}");
        var themeUri = new Uri(isDark ? "Themes/DarkTheme.xaml" : "Themes/LightTheme.xaml", UriKind.Relative);
        var resourceDict = (ResourceDictionary)LoadComponent(themeUri);
        Current.Resources.MergedDictionaries.Clear();
        Current.Resources.MergedDictionaries.Add(resourceDict);
    }
}
