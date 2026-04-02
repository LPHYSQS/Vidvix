using System;
using System.Collections.Generic;
using Vidvix.Core.Models;

namespace Vidvix.Core.Interfaces;

public interface ILogger
{
    event EventHandler<LogEntry>? EntryLogged;

    IReadOnlyList<LogEntry> Entries { get; }

    void Log(LogLevel level, string message, Exception? exception = null);
}

