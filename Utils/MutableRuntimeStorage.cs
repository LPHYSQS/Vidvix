using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Vidvix.Utils;

public static class MutableRuntimeStorage
{
    private const int ErrorSuccess = 0;
    private const int ErrorInsufficientBuffer = 122;
    private static readonly Lazy<bool> HasPackageIdentityValue = new(ResolveHasPackageIdentity);

    public static bool HasPackageIdentity => HasPackageIdentityValue.Value;

    public static bool ShouldPreferExecutableDirectory => !HasPackageIdentity;

    public static string ResolveWritableStorageRootPath(
        string localDataDirectoryName,
        params string[] storageSegments)
    {
        var applicationStorageRootPath = GetApplicationStorageRootPath(storageSegments);
        if (ShouldPreferExecutableDirectory && CanWriteToDirectory(applicationStorageRootPath))
        {
            return applicationStorageRootPath;
        }

        var localStorageRootPath = GetLocalStorageRootPath(localDataDirectoryName, storageSegments);
        Directory.CreateDirectory(localStorageRootPath);
        return localStorageRootPath;
    }

    public static string GetApplicationStorageRootPath(params string[] storageSegments) =>
        CombinePath(ApplicationPaths.ExecutableDirectoryPath, storageSegments);

    public static string GetLocalStorageRootPath(
        string localDataDirectoryName,
        params string[] storageSegments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(localDataDirectoryName);
        ArgumentNullException.ThrowIfNull(storageSegments);

        // External runtimes such as Demucs Python, FFmpeg, and NCNN helpers can be launched as
        // regular Win32 child processes even when the host app itself has package identity.
        // Keep their mutable storage under the user's public LocalAppData root so those child
        // processes inherit a stable, user-owned ACL instead of relying on package-private cache
        // semantics that can differ across Store environments.
        var storageRootPath = CombinePath(
            GetUserLocalAppDataRootPath(),
            new[] { localDataDirectoryName });

        return CombinePath(storageRootPath, storageSegments);
    }

    public static bool CanWriteToDirectory(string directoryPath)
    {
        try
        {
            Directory.CreateDirectory(directoryPath);

            var probeFilePath = Path.Combine(directoryPath, $".write-probe-{Guid.NewGuid():N}.tmp");
            using (File.Create(probeFilePath, 1, FileOptions.DeleteOnClose))
            {
            }

            if (File.Exists(probeFilePath))
            {
                File.Delete(probeFilePath);
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool ResolveHasPackageIdentity()
    {
        try
        {
            var packageFullNameLength = 0;
            var result = GetCurrentPackageFullName(ref packageFullNameLength, IntPtr.Zero);
            return result is ErrorSuccess or ErrorInsufficientBuffer;
        }
        catch
        {
            return false;
        }
    }

    private static string GetUserLocalAppDataRootPath()
    {
        var localAppDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return string.IsNullOrWhiteSpace(localAppDataPath)
            ? Path.Combine(ApplicationPaths.ExecutableDirectoryPath, ".local")
            : Path.GetFullPath(localAppDataPath);
    }

    private static string CombinePath(string rootPath, params string[] segments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        ArgumentNullException.ThrowIfNull(segments);

        var path = Path.GetFullPath(rootPath);
        foreach (var segment in segments)
        {
            path = Path.Combine(path, segment);
        }

        return path;
    }

    [DllImport("kernel32.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern int GetCurrentPackageFullName(ref int packageFullNameLength, IntPtr packageFullName);
}
