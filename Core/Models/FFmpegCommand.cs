using System;
using System.Collections.Generic;
using System.Linq;

namespace Vidvix.Core.Models;

public sealed class FFmpegCommand
{
    public FFmpegCommand(string executablePath, IReadOnlyList<string> arguments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        ArgumentNullException.ThrowIfNull(arguments);

        ExecutablePath = executablePath;
        Arguments = arguments;
    }

    public string ExecutablePath { get; }

    public IReadOnlyList<string> Arguments { get; }

    public string DisplayCommand => Format(ExecutablePath, Arguments);

    private static string Format(string executablePath, IEnumerable<string> arguments)
    {
        var tokens = new[] { Quote(executablePath) }
            .Concat(arguments.Select(Quote));

        return string.Join(" ", tokens);
    }

    private static string Quote(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "\"\"";
        }

        if (!value.Contains(' ') && !value.Contains('"'))
        {
            return value;
        }

        return $"\"{value.Replace("\"", "\\\"")}\"";
    }
}
