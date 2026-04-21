using System;
using System.Diagnostics;
using System.IO;

namespace Vidvix.Utils;

public static class ApplicationPaths
{
    private static readonly Lazy<string> ExecutablePathValue = new(ResolveExecutablePath);
    private static readonly Lazy<string> ExecutableDirectoryPathValue = new(ResolveExecutableDirectoryPath);

    public static string RuntimeBaseDirectoryPath => AppContext.BaseDirectory;

    public static string ExecutablePath => ExecutablePathValue.Value;

    public static string ExecutableDirectoryPath => ExecutableDirectoryPathValue.Value;

    public static string CombineFromExecutableDirectory(params string[] segments)
    {
        ArgumentNullException.ThrowIfNull(segments);

        var path = ExecutableDirectoryPath;
        foreach (var segment in segments)
        {
            path = Path.Combine(path, segment);
        }

        return path;
    }

    private static string ResolveExecutablePath()
    {
        if (!string.IsNullOrWhiteSpace(Environment.ProcessPath))
        {
            return Path.GetFullPath(Environment.ProcessPath);
        }

        try
        {
            using var process = Process.GetCurrentProcess();
            var processPath = process.MainModule?.FileName;
            if (!string.IsNullOrWhiteSpace(processPath))
            {
                return Path.GetFullPath(processPath);
            }
        }
        catch
        {
        }

        return Path.GetFullPath(AppContext.BaseDirectory);
    }

    private static string ResolveExecutableDirectoryPath()
    {
        var runtimeBaseDirectory = AppContext.BaseDirectory;
        if (!string.IsNullOrWhiteSpace(runtimeBaseDirectory) &&
            Directory.Exists(runtimeBaseDirectory))
        {
            return Path.GetFullPath(runtimeBaseDirectory);
        }

        var executablePath = ResolveExecutablePath();
        if (Directory.Exists(executablePath))
        {
            return executablePath;
        }

        var directoryPath = Path.GetDirectoryName(executablePath);
        return string.IsNullOrWhiteSpace(directoryPath)
            ? Path.GetFullPath(AppContext.BaseDirectory)
            : Path.GetFullPath(directoryPath);
    }
}
