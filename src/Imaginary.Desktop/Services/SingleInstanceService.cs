using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using Imaginary.Core.Logging;

namespace Imaginary.Desktop.Services;

public static class SingleInstanceService
{
    private const string MutexName = @"Global\Imaginary_App_SingleInstance_Mutex";
    private const string PipeName = "Imaginary_App_SingleInstance_IPC_Pipe";
    private static Mutex? _mutex;
    private static bool _ownsMutex;
    private static CancellationTokenSource? _pipeCts;

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    private const int SW_RESTORE = 9;

    public static bool TryRegisterSingleInstance(string[] startupArgs, Action<string[]> onArgsReceived)
    {
        bool createdNew;
        try
        {
            _mutex = new Mutex(true, MutexName, out createdNew);
        }
        catch (AbandonedMutexException)
        {
            createdNew = true;
        }

        if (createdNew)
        {
            _ownsMutex = true;
            AppLogger.Debug("SingleInstance", "Primäre Instanz erfolgreich registriert (Mutex akquiriert).");
            // First/primary instance: start pipe server in background
            _pipeCts = new CancellationTokenSource();
            StartPipeServer(onArgsReceived, _pipeCts.Token);
            return true;
        }
        else
        {
            _ownsMutex = false;
            try { _mutex?.Dispose(); } catch { }
            _mutex = null;

            AppLogger.Info("SingleInstance", $"Folgeinstanz erkannt: Sende {startupArgs?.Length ?? 0} Argument(e) an primäre Instanz...");
            // Secondary instance: send args to primary instance via named pipe and exit
            SendArgsToPrimaryInstance(startupArgs ?? Array.Empty<string>());
            return false;
        }
    }

    public static void ActivateWindow(IntPtr handle)
    {
        if (handle != IntPtr.Zero)
        {
            ShowWindow(handle, SW_RESTORE);
            SetForegroundWindow(handle);
        }
    }

    private static void SendArgsToPrimaryInstance(string[] args)
    {
        if (args == null || args.Length == 0) return;

        // Try to connect to pipe server with retry in case primary is still initializing
        for (int retry = 0; retry < 5; retry++)
        {
            try
            {
                using var pipeClient = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
                pipeClient.Connect(1500);

                using var writer = new StreamWriter(pipeClient, Encoding.UTF8);
                foreach (var arg in args)
                {
                    writer.WriteLine(arg);
                }
                writer.Flush();
                return;
            }
            catch
            {
                Thread.Sleep(100);
            }
        }
    }

    private static void StartPipeServer(Action<string[]> onArgsReceived, CancellationToken ct)
    {
        Task.Run(async () =>
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    using var server = new NamedPipeServerStream(
                        PipeName,
                        PipeDirection.In,
                        NamedPipeServerStream.MaxAllowedServerInstances,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous);

                    await server.WaitForConnectionAsync(ct);

                    using var reader = new StreamReader(server, Encoding.UTF8);
                    var receivedLines = new List<string>();
                    string? line;
                    while ((line = await reader.ReadLineAsync()) != null)
                    {
                        if (!string.IsNullOrWhiteSpace(line))
                        {
                            receivedLines.Add(line.Trim('\"'));
                        }
                    }

                    if (receivedLines.Count > 0)
                    {
                        onArgsReceived(receivedLines.ToArray());
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch
                {
                    // Delay slightly to prevent tight loop on transient error
                    await Task.Delay(150, ct);
                }
            }
        }, ct);
    }

    public static void Cleanup()
    {
        try
        {
            _pipeCts?.Cancel();
            _pipeCts?.Dispose();
        }
        catch { }

        if (_ownsMutex && _mutex != null)
        {
            try
            {
                _mutex.ReleaseMutex();
            }
            catch { }
        }

        try
        {
            _mutex?.Dispose();
        }
        catch { }

        _mutex = null;
        _ownsMutex = false;
    }
}
