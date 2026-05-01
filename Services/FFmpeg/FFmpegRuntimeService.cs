using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Vidvix.Core.Interfaces;
using Vidvix.Core.Models;
using Vidvix.Utils;

namespace Vidvix.Services.FFmpeg;

public sealed class FFmpegRuntimeService : IFFmpegRuntimeService
{
    private readonly ApplicationConfiguration _configuration;
    private readonly IFFmpegPackageSource _packageSource;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _syncLock = new(1, 1);
    private FFmpegRuntimeResolution? _cachedResolution;

    public FFmpegRuntimeService(
        ApplicationConfiguration configuration,
        IFFmpegPackageSource packageSource,
        ILogger logger)
    {
        _configuration = configuration;
        _packageSource = packageSource;
        _logger = logger;
    }

    public async Task<FFmpegRuntimeResolution> EnsureAvailableAsync(CancellationToken cancellationToken = default)
    {
        if (TryGetCachedResolution(out var cachedResolution))
        {
            return cachedResolution;
        }

        await _syncLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (TryGetCachedResolution(out cachedResolution))
            {
                return cachedResolution;
            }

            var bundledRuntimeRootPath = GetBundledRuntimeRootPath();
            var bundledExecutablePath = FindExecutablePath(bundledRuntimeRootPath);

            if (!string.IsNullOrWhiteSpace(bundledExecutablePath))
            {
                return CacheResolution(bundledExecutablePath, bundledRuntimeRootPath, wasDownloaded: false);
            }

            var applicationRootPath = GetApplicationRuntimeRootPath();
            var legacyRuntimeDirectoryPath = Path.Combine(applicationRootPath, _configuration.RuntimeCurrentVersionDirectoryName);
            var legacyExecutablePath = FindExecutablePath(legacyRuntimeDirectoryPath);

            if (!string.IsNullOrWhiteSpace(legacyExecutablePath))
            {
                return CacheResolution(legacyExecutablePath, applicationRootPath, wasDownloaded: false);
            }

            var storageRootPath = ResolveWritableStorageRootPath();
            var currentRuntimeDirectoryPath = Path.Combine(storageRootPath, _configuration.RuntimeCurrentVersionDirectoryName);
            var executablePath = FindExecutablePath(currentRuntimeDirectoryPath);

            if (!string.IsNullOrWhiteSpace(executablePath))
            {
                return CacheResolution(executablePath, storageRootPath, wasDownloaded: false);
            }

            var manifest = await _packageSource.GetLatestPackageAsync(cancellationToken).ConfigureAwait(false);
            var runtimePreparation = await DownloadAndExtractRuntimeWithFallbackAsync(
                manifest,
                storageRootPath,
                cancellationToken).ConfigureAwait(false);
            executablePath = runtimePreparation.ExecutablePath;
            storageRootPath = runtimePreparation.StorageRootPath;

            return CacheResolution(executablePath, storageRootPath, wasDownloaded: true);
        }
        finally
        {
            _syncLock.Release();
        }
    }

    private bool TryGetCachedResolution(out FFmpegRuntimeResolution resolution)
    {
        if (_cachedResolution is not null && File.Exists(_cachedResolution.ExecutablePath))
        {
            resolution = _cachedResolution;
            return true;
        }

        resolution = null!;
        return false;
    }

    private FFmpegRuntimeResolution CacheResolution(string executablePath, string storageRootPath, bool wasDownloaded)
    {
        _cachedResolution = new FFmpegRuntimeResolution
        {
            ExecutablePath = executablePath,
            StorageRootPath = storageRootPath,
            WasDownloaded = wasDownloaded
        };

        return _cachedResolution;
    }

    private async Task<string> DownloadArchiveAsync(
        FFmpegPackageManifest manifest,
        string storageRootPath,
        CancellationToken cancellationToken)
    {
        var downloadDirectoryPath = Path.Combine(storageRootPath, _configuration.RuntimeDownloadCacheDirectoryName);
        Directory.CreateDirectory(downloadDirectoryPath);

        var archivePath = Path.Combine(downloadDirectoryPath, manifest.ArchiveFileName);

        if (await IsArchiveUsableAsync(archivePath, manifest.ExpectedSha256, cancellationToken).ConfigureAwait(false))
        {
            _logger.Log(LogLevel.Info, "已复用本地缓存的 FFmpeg 安装包。");
            return archivePath;
        }

        _logger.Log(LogLevel.Info, "正在下载本地 FFmpeg 运行时...");

        if (File.Exists(archivePath))
        {
            File.Delete(archivePath);
        }

        using var client = CreateHttpClient();
        using var response = await client.GetAsync(
            manifest.ArchiveUri,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        await using (var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
        await using (var fileStream = new FileStream(archivePath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            await responseStream.CopyToAsync(fileStream, cancellationToken).ConfigureAwait(false);
        }

        if (!await IsArchiveUsableAsync(archivePath, manifest.ExpectedSha256, cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException("已下载的 FFmpeg 安装包校验失败，请稍后重试。");
        }

        return archivePath;
    }

    private async Task<string> ExtractRuntimeAsync(
        string archivePath,
        string storageRootPath,
        string currentRuntimeDirectoryPath,
        CancellationToken cancellationToken)
    {
        _logger.Log(LogLevel.Info, "正在解压本地 FFmpeg 运行时...");

        var stagingDirectoryPath = Path.Combine(storageRootPath, $".runtime-staging-{Guid.NewGuid():N}");
        Directory.CreateDirectory(stagingDirectoryPath);

        try
        {
            await Task.Run(() => ZipFile.ExtractToDirectory(archivePath, stagingDirectoryPath), cancellationToken).ConfigureAwait(false);

            var extractedExecutablePath = FindExecutablePath(stagingDirectoryPath)
                ?? throw new InvalidOperationException("FFmpeg 安装包中缺少可执行文件。");

            var extractedBinDirectory = Directory.GetParent(extractedExecutablePath)?.FullName
                ?? throw new InvalidOperationException("FFmpeg 安装包目录结构无效。");

            var packageRootDirectory = Directory.GetParent(extractedBinDirectory)?.FullName
                ?? throw new InvalidOperationException("FFmpeg 安装包目录结构无效。");

            if (Directory.Exists(currentRuntimeDirectoryPath))
            {
                Directory.Delete(currentRuntimeDirectoryPath, recursive: true);
            }

            Directory.CreateDirectory(currentRuntimeDirectoryPath);
            MoveDirectoryIfExists(packageRootDirectory, currentRuntimeDirectoryPath, "bin");
            MoveDirectoryIfExists(packageRootDirectory, currentRuntimeDirectoryPath, "presets");
            MoveFileIfExists(packageRootDirectory, currentRuntimeDirectoryPath, "LICENSE.txt");

            var resolvedExecutablePath = FindExecutablePath(currentRuntimeDirectoryPath)
                ?? throw new InvalidOperationException("解压后的 FFmpeg 目录中没有找到可执行文件。");

            TryDeleteDirectory(stagingDirectoryPath);
            _logger.Log(LogLevel.Info, "本地 FFmpeg 运行时准备完成。");
            return resolvedExecutablePath;
        }
        catch
        {
            TryDeleteDirectory(stagingDirectoryPath);
            TryDeleteDirectory(currentRuntimeDirectoryPath);
            throw;
        }
    }

    private async Task<(string ExecutablePath, string StorageRootPath)> DownloadAndExtractRuntimeWithFallbackAsync(
        FFmpegPackageManifest manifest,
        string storageRootPath,
        CancellationToken cancellationToken)
    {
        try
        {
            var currentRuntimeDirectoryPath = Path.Combine(storageRootPath, _configuration.RuntimeCurrentVersionDirectoryName);
            var archivePath = await DownloadArchiveAsync(manifest, storageRootPath, cancellationToken).ConfigureAwait(false);
            var executablePath = await ExtractRuntimeAsync(
                archivePath,
                storageRootPath,
                currentRuntimeDirectoryPath,
                cancellationToken).ConfigureAwait(false);
            return (executablePath, storageRootPath);
        }
        catch (Exception exception) when (ShouldRetryWithFallbackStorage(storageRootPath, exception))
        {
            var fallbackStorageRootPath = GetFallbackStorageRootPath();
            Directory.CreateDirectory(fallbackStorageRootPath);
            _logger.Log(LogLevel.Warning, "FFmpeg 运行时无法写入应用目录，正在回退到用户本地可写目录。", exception);

            var currentRuntimeDirectoryPath = Path.Combine(fallbackStorageRootPath, _configuration.RuntimeCurrentVersionDirectoryName);
            var archivePath = await DownloadArchiveAsync(manifest, fallbackStorageRootPath, cancellationToken).ConfigureAwait(false);
            var executablePath = await ExtractRuntimeAsync(
                archivePath,
                fallbackStorageRootPath,
                currentRuntimeDirectoryPath,
                cancellationToken).ConfigureAwait(false);
            return (executablePath, fallbackStorageRootPath);
        }
    }

    private async Task<bool> IsArchiveUsableAsync(
        string archivePath,
        string? expectedSha256,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(archivePath))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(expectedSha256))
        {
            return true;
        }

        var actualSha256 = await ComputeSha256Async(archivePath, cancellationToken).ConfigureAwait(false);
        return string.Equals(actualSha256, expectedSha256, StringComparison.OrdinalIgnoreCase);
    }

    private string GetBundledRuntimeRootPath() =>
        MutableRuntimeStorage.GetApplicationStorageRootPath(
            _configuration.RuntimeDirectoryName,
            _configuration.BundledRuntimeDirectoryName);

    private string GetApplicationRuntimeRootPath() =>
        MutableRuntimeStorage.GetApplicationStorageRootPath(
            _configuration.RuntimeDirectoryName,
            _configuration.RuntimeVendorDirectoryName);

    private string GetFallbackStorageRootPath() =>
        MutableRuntimeStorage.GetLocalStorageRootPath(
            _configuration.LocalDataDirectoryName,
            _configuration.RuntimeDirectoryName,
            _configuration.RuntimeVendorDirectoryName);

    private string ResolveWritableStorageRootPath() =>
        MutableRuntimeStorage.ResolveWritableStorageRootPath(
            _configuration.LocalDataDirectoryName,
            _configuration.RuntimeDirectoryName,
            _configuration.RuntimeVendorDirectoryName);

    private string? FindExecutablePath(string rootDirectoryPath)
    {
        if (!Directory.Exists(rootDirectoryPath))
        {
            return null;
        }

        foreach (var filePath in Directory.EnumerateFiles(
                     rootDirectoryPath,
                     _configuration.FFmpegExecutableFileName,
                     SearchOption.AllDirectories))
        {
            return filePath;
        }

        return null;
    }

    private bool ShouldRetryWithFallbackStorage(string storageRootPath, Exception exception) =>
        IsApplicationStorageRootPath(storageRootPath) &&
        exception is IOException or UnauthorizedAccessException &&
        !string.Equals(storageRootPath, GetFallbackStorageRootPath(), StringComparison.OrdinalIgnoreCase);

    private bool IsApplicationStorageRootPath(string storageRootPath) =>
        string.Equals(storageRootPath, GetApplicationRuntimeRootPath(), StringComparison.OrdinalIgnoreCase);

    private static async Task<string> ComputeSha256Async(string filePath, CancellationToken cancellationToken)
    {
        await using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var sha256 = SHA256.Create();
        var hash = await sha256.ComputeHashAsync(fileStream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash);
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Vidvix", "1.0"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/octet-stream"));
        return client;
    }

    private static void MoveDirectoryIfExists(string sourceRootPath, string targetRootPath, string directoryName)
    {
        var sourcePath = Path.Combine(sourceRootPath, directoryName);
        if (!Directory.Exists(sourcePath))
        {
            return;
        }

        Directory.Move(sourcePath, Path.Combine(targetRootPath, directoryName));
    }

    private static void MoveFileIfExists(string sourceRootPath, string targetRootPath, string fileName)
    {
        var sourcePath = Path.Combine(sourceRootPath, fileName);
        if (!File.Exists(sourcePath))
        {
            return;
        }

        File.Move(sourcePath, Path.Combine(targetRootPath, fileName));
    }

    private static void TryDeleteDirectory(string directoryPath)
    {
        try
        {
            if (Directory.Exists(directoryPath))
            {
                Directory.Delete(directoryPath, recursive: true);
            }
        }
        catch
        {
        }
    }
}
