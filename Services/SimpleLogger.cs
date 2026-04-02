using System;
using System.Collections.Generic;
using Vidvix.Core.Interfaces;
using Vidvix.Core.Models;

namespace Vidvix.Services;

public sealed class SimpleLogger : ILogger
{
    private readonly object _syncRoot = new();
    private readonly List<LogEntry> _entries = new();
    private readonly bool _mirrorToConsole;

    public SimpleLogger(bool mirrorToConsole)
    {
        _mirrorToConsole = mirrorToConsole;
    }

    public event EventHandler<LogEntry>? EntryLogged;

    public IReadOnlyList<LogEntry> Entries
    {
        get
        {
            lock (_syncRoot)
            {
                return _entries.ToArray();
            }
        }
    }

    public void Log(LogLevel level, string message, Exception? exception = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        var effectiveMessage = exception is null
            ? message
            : $"{message} {exception.Message}";

        var entry = new LogEntry(DateTimeOffset.Now, level, effectiveMessage);

        lock (_syncRoot)
        {
            _entries.Add(entry);
        }

        if (_mirrorToConsole)
        {
            Console.WriteLine($"[{entry.DisplayTimestamp}] [{entry.DisplayLevel}] {entry.Message}");
        }

        EntryLogged?.Invoke(this, entry);
    }
}

