using System;
using System.Collections.Generic;

namespace Imaginary.Core.Logging;

public enum LogLevel
{
    Debug,
    Information,
    Warning,
    Error
}

public record LogEntry(
    DateTime Timestamp,
    LogLevel Level,
    string Source,
    string Message,
    string? ExceptionDetails = null)
{
    public string FormattedTime => Timestamp.ToString("HH:mm:ss.fff");
    public string FormattedTimestamp => Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff");

    public override string ToString()
    {
        var levelStr = Level switch
        {
            LogLevel.Debug => "DEBUG",
            LogLevel.Information => "INFO ",
            LogLevel.Warning => "WARN ",
            LogLevel.Error => "ERROR",
            _ => "INFO "
        };
        var res = $"[{FormattedTimestamp}] [{levelStr}] [{Source}] {Message}";
        if (!string.IsNullOrEmpty(ExceptionDetails))
        {
            res += $"\n{ExceptionDetails}";
        }
        return res;
    }
}

public interface ILogService
{
    void Log(LogLevel level, string source, string message, Exception? ex = null);
    void Debug(string source, string message);
    void Info(string source, string message);
    void Warn(string source, string message, Exception? ex = null);
    void Error(string source, string message, Exception? ex = null);

    IReadOnlyList<LogEntry> GetRecentEntries();
    void Clear();
    event Action<LogEntry>? EntryLogged;
    string? LogFilePath { get; }
}
