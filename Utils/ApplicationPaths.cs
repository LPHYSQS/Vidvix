using System;
using System.Diagnostics;
using System.IO;

namespace Vidvix.Utils;

public static class ApplicationPaths
{
    private static readonly string[] ApplicationRootMarkerRelativePaths =
    {
        Path.Combine("Assets", "logo.ico"),
        Path.Combine("Resources", "Localization", "manifest.json"),
        "Tools"
    };

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
        var runtimeBaseDirectory = ResolveRuntimeBaseDirectoryPath();
        var executablePath = ResolveExecutablePath();
        var executableDirectory = TryResolveContainingDirectory(executablePath);

        if (LooksLikeApplicationRoot(executableDirectory))
        {
            return executableDirectory!;
        }

        if (LooksLikeApplicationRoot(runtimeBaseDirectory))
        {
            return runtimeBaseDirectory;
        }

        if (!string.IsNullOrWhiteSpace(executableDirectory) &&
            Directory.Exists(executableDirectory))
        {
            return Path.GetFullPath(executableDirectory);
        }

        return Directory.Exists(runtimeBaseDirectory)
            ? runtimeBaseDirectory
            : Path.GetFullPath(".");
    }

    private static string? TryResolveContainingDirectory(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        if (File.Exists(path))
        {
            var containingDirectory = Path.GetDirectoryName(path);
            return string.IsNullOrWhiteSpace(containingDirectory)
                ? null
                : Path.GetFullPath(containingDirectory);
        }

        return Directory.Exists(path)
            ? Path.GetFullPath(path)
            : null;
    }

    private static bool LooksLikeApplicationRoot(string? candidateDirectory)
    {
        if (string.IsNullOrWhiteSpace(candidateDirectory) ||
            !Directory.Exists(candidateDirectory))
        {
            return false;
        }

        foreach (var relativePath in ApplicationRootMarkerRelativePaths)
        {
            var candidatePath = Path.Combine(candidateDirectory, relativePath);
            if (File.Exists(candidatePath) || Directory.Exists(candidatePath))
            {
                return true;
            }
        }

        return false;
    }
}
