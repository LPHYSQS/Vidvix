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

    public string DisplayLevel => Level.ToString().ToUpperInvariant();
}

