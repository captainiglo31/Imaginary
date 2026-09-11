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

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

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
                AppLogger.Info("SingleInstance", $"Aktivierungssignal / IPC empfangen ({files.Length} Datei(en))");
                if (files.Length > 0)
                {
                    FilesReceivedViaIpc?.Invoke(files);
                }

                if (Current.MainWindow != null)
                {
                    if (!Current.MainWindow.IsVisible)
                    {
                        Current.MainWindow.Show();
                    }
                    if (Current.MainWindow.WindowState == WindowState.Minimized)
                    {
                        Current.MainWindow.WindowState = WindowState.Normal;
                    }
                    Current.MainWindow.Activate();
                    Current.MainWindow.Focus();
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

        ShutdownMode = ShutdownMode.OnMainWindowClose;

        bool startInTray = e.Args.Any(a => string.Equals(a, "--tray", StringComparison.OrdinalIgnoreCase) ||
                                           string.Equals(a, "--minimized", StringComparison.OrdinalIgnoreCase));

        if (startInTray)
        {
            AppLogger.Info("App", "Startparameter --tray / --minimized erkannt – Starte lautlos im Hintergrund.");
            var mainWindow = new MainWindow();
            MainWindow = mainWindow;

            if (mainWindow.DataContext is ViewModels.MainViewModel vm && vm.ShowTrayNotifications)
            {
                _ = Task.Run(async () =>
                {
                    await Task.Delay(2000);
                    Current.Dispatcher.Invoke(() =>
                    {
                        vm.TrayService?.ShowNotification(
                            "Imaginary ist aktiv",
                            "Imaginary läuft minimiert im Infobereich. Klicke auf das Symbol oder die Benachrichtigung, um das Fenster zu öffnen.",
                            System.Windows.Forms.ToolTipIcon.Info);
                    });
                });
            }
            return;
        }

        Views.SplashScreenWindow? splash = null;
        try
        {
            splash = new Views.SplashScreenWindow();
            splash.Show();
        }
        catch (Exception ex)
        {
            AppLogger.Warn("App", "Splash Screen konnte nicht angezeigt werden", ex);
        }

        // Zeige Splash Screen für ca. 1.1s für einen flüssigen, coolen Start
        if (splash != null)
        {
            await Task.Delay(1100);
        }

        var mainWindowNormal = new MainWindow();
        MainWindow = mainWindowNormal;
        mainWindowNormal.Show();

        if (splash != null)
        {
            await splash.FadeOutAndCloseAsync();
        }
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
