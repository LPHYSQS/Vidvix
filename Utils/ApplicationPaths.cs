using System;
using System.Diagnostics;
using System.IO;

namespace Vidvix.Utils;

public static class ApplicationPaths
{
    private static readonly Lazy<string> RuntimeBaseDirectoryPathValue = new(ResolveRuntimeBaseDirectoryPath);
    private static readonly Lazy<string> ExecutablePathValue = new(ResolveExecutablePath);
    private static readonly Lazy<string> ExecutableDirectoryPathValue = new(ResolveExecutableDirectoryPath);

    public static string RuntimeBaseDirectoryPath => RuntimeBaseDirectoryPathValue.Value;

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

    private static string ResolveRuntimeBaseDirectoryPath()
    {
        var runtimeBaseDirectory = AppContext.BaseDirectory;
        return string.IsNullOrWhiteSpace(runtimeBaseDirectory)
            ? Path.GetFullPath(".")
            : Path.GetFullPath(runtimeBaseDirectory);
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
        var executablePath = ResolveExecutablePath();
        if (File.Exists(executablePath))
        {
            var executableDirectory = Path.GetDirectoryName(executablePath);
            if (!string.IsNullOrWhiteSpace(executableDirectory) &&
                Directory.Exists(executableDirectory))
            {
                return Path.GetFullPath(executableDirectory);
            }
        }

        if (Directory.Exists(executablePath))
        {
            return executablePath;
        }

        var runtimeBaseDirectory = ResolveRuntimeBaseDirectoryPath();
        return Directory.Exists(runtimeBaseDirectory)
            ? runtimeBaseDirectory
            : Path.GetFullPath(".");
    }
}
