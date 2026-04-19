using System;
using System.Collections.Generic;
using System.IO;
using Vidvix.Core.Interfaces;
using Vidvix.Core.Models;
using Vidvix.Utils;

namespace Vidvix.Services;

public sealed class SimpleLogger : ILogger
{
    private readonly object _syncRoot = new();
    private readonly List<LogEntry> _entries = new();
    private readonly bool _mirrorToConsole;
    private readonly string? _logFilePath;

    public SimpleLogger(bool mirrorToConsole)
    {
        _mirrorToConsole = mirrorToConsole;
        _logFilePath = TryResolveLogFilePath();
        TryWriteSessionBanner();
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

        TryAppendToLogFile(entry);

        EntryLogged?.Invoke(this, entry);
    }

    private string? TryResolveLogFilePath()
    {
        try
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(localAppData))
            {
                return null;
            }

            var logDirectory = Path.Combine(localAppData, "Vidvix", "Logs");
            Directory.CreateDirectory(logDirectory);
            return Path.Combine(logDirectory, "latest.log");
        }
        catch
        {
            return null;
        }
    }

    private void TryWriteSessionBanner()
    {
        if (string.IsNullOrWhiteSpace(_logFilePath))
        {
            return;
        }

        try
        {
            File.AppendAllText(
                _logFilePath,
                $"{Environment.NewLine}===== Session {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz} | BaseDir={ApplicationPaths.RuntimeBaseDirectoryPath} | ExeDir={ApplicationPaths.ExecutableDirectoryPath} ====={Environment.NewLine}");
        }
        catch
        {
        }
    }

    private void TryAppendToLogFile(LogEntry entry)
    {
        if (string.IsNullOrWhiteSpace(_logFilePath))
        {
            return;
        }

        try
        {
            File.AppendAllText(
                _logFilePath,
                $"[{entry.DisplayTimestamp}] [{entry.DisplayLevel}] {entry.Message}{Environment.NewLine}");
        }
        catch
        {
        }
    }
}
