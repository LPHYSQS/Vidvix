using System;

namespace Vidvix.Core.Models;

public sealed class LogEntry
{
    public LogEntry(DateTimeOffset timestamp, LogLevel level, string message)
    {
        Timestamp = timestamp;
        Level = level;
        Message = message;
    }

    public DateTimeOffset Timestamp { get; }

    public LogLevel Level { get; }

    public string Message { get; }

    public string DisplayTimestamp => Timestamp.ToLocalTime().ToString("HH:mm:ss");

    public string DisplayLevel => Level switch
    {
        LogLevel.Info => "提示",
        LogLevel.Warning => "警告",
        LogLevel.Error => "错误",
        _ => "提示"
    };
}
