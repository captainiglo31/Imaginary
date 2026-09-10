using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Imaginary.Core.Logging;

public class LogService : ILogService
{
    private static readonly Lazy<LogService> _default = new(() => new LogService());
    public static LogService Default => _default.Value;

    private readonly object _lock = new();
    private readonly List<LogEntry> _entries = new();
    private readonly int _maxEntries;
    private readonly string? _logFilePath;

    public event Action<LogEntry>? EntryLogged;
    public string? LogFilePath => _logFilePath;

    public LogService(int maxEntries = 2000, string? customLogDir = null)
    {
        _maxEntries = Math.Max(100, maxEntries);

        try
        {
            var logDir = customLogDir;
            if (string.IsNullOrEmpty(logDir))
            {
                // Try portable path first if writable or next to app
                var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                var portableLogs = Path.Combine(baseDir, "logs");
                if (Directory.Exists(portableLogs) || Directory.Exists(Path.Combine(baseDir, "portable-assets")))
                {
                    logDir = portableLogs;
                }
                else
                {
                    var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                    logDir = Path.Combine(localAppData, "Imaginary", "logs");
                }
            }

            Directory.CreateDirectory(logDir);
            var fileName = $"imaginary-{DateTime.Now:yyyyMMdd}.log";
            _logFilePath = Path.Combine(logDir, fileName);
        }
        catch
        {
            _logFilePath = null;
        }
    }

    public void Log(LogLevel level, string source, string message, Exception? ex = null)
    {
        string? exDetails = null;
        if (ex != null)
        {
            exDetails = $"{ex.GetType().FullName}: {ex.Message}\n{ex.StackTrace}";
            if (ex.InnerException != null)
            {
                exDetails += $"\nInner: {ex.InnerException.GetType().FullName}: {ex.InnerException.Message}\n{ex.InnerException.StackTrace}";
            }
        }

        var entry = new LogEntry(DateTime.Now, level, source, message, exDetails);

        lock (_lock)
        {
            if (_entries.Count >= _maxEntries)
            {
                _entries.RemoveAt(0);
            }
            _entries.Add(entry);

            WriteToFile(entry);
        }

        try
        {
            EntryLogged?.Invoke(entry);
        }
        catch
        {
            // Do not let listener exceptions crash logging
        }
    }

    public void Debug(string source, string message) => Log(LogLevel.Debug, source, message);
    public void Info(string source, string message) => Log(LogLevel.Information, source, message);
    public void Warn(string source, string message, Exception? ex = null) => Log(LogLevel.Warning, source, message, ex);
    public void Error(string source, string message, Exception? ex = null) => Log(LogLevel.Error, source, message, ex);

    public IReadOnlyList<LogEntry> GetRecentEntries()
    {
        lock (_lock)
        {
            return _entries.ToList();
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _entries.Clear();
        }
    }

    private void WriteToFile(LogEntry entry)
    {
        if (string.IsNullOrEmpty(_logFilePath)) return;

        try
        {
            File.AppendAllText(_logFilePath, entry.ToString() + Environment.NewLine);
        }
        catch
        {
            // Silently ignore disk write issues
        }
    }
}

public static class AppLogger
{
    public static ILogService Instance => LogService.Default;

    public static void Debug(string source, string message) => Instance.Debug(source, message);
    public static void Info(string source, string message) => Instance.Info(source, message);
    public static void Warn(string source, string message, Exception? ex = null) => Instance.Warn(source, message, ex);
    public static void Error(string source, string message, Exception? ex = null) => Instance.Error(source, message, ex);
}
