using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Vidvix.Core.Interfaces;
using Vidvix.Core.Models;

namespace Vidvix.Services.Demucs;

public sealed class DemucsRuntimeService : IDemucsRuntimeService
{
    private readonly ApplicationConfiguration _configuration;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _syncLock = new(1, 1);
    private DemucsRuntimeResolution? _cachedResolution;

    public DemucsRuntimeService(
        ApplicationConfiguration configuration,
        ILogger logger)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<DemucsRuntimeResolution> EnsureAvailableAsync(CancellationToken cancellationToken = default)
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

            var storageRootPath = ResolveWritableStorageRootPath();
            Directory.CreateDirectory(storageRootPath);

            var runtimeRootPath = FindAvailableRuntimeRootPath(storageRootPath);
            var modelRepositoryPath = FindAvailableModelRepositoryPath(storageRootPath);
            var wasExtracted = false;

            if (string.IsNullOrWhiteSpace(runtimeRootPath))
            {
                var runtimeArchivePath = GetBundledRuntimeArchivePath();
                if (!File.Exists(runtimeArchivePath))
                {
                    throw new InvalidOperationException(
                        $"未找到离线 Demucs 运行时包，请补齐 {Path.Combine("Tools", _configuration.DemucsDirectoryName, _configuration.DemucsPackagesDirectoryName, _configuration.DemucsRuntimeArchiveFileName)}。");
                }

                runtimeRootPath = await ExtractRuntimeArchiveAsync(
                    runtimeArchivePath,
                    storageRootPath,
                    cancellationToken).ConfigureAwait(false);
                wasExtracted = true;
            }

            if (string.IsNullOrWhiteSpace(modelRepositoryPath))
            {
                var modelArchivePath = GetBundledModelArchivePath();
                if (!File.Exists(modelArchivePath))
                {
                    throw new InvalidOperationException(
                        $"未找到 Demucs 模型仓，请补齐 {Path.Combine("Tools", _configuration.DemucsDirectoryName, _configuration.DemucsModelsDirectoryName)} 或 {Path.Combine("Tools", _configuration.DemucsDirectoryName, _configuration.DemucsPackagesDirectoryName, _configuration.DemucsModelArchiveFileName)}。");
                }

                modelRepositoryPath = await ExtractModelArchiveAsync(
                    modelArchivePath,
                    storageRootPath,
                    cancellationToken).ConfigureAwait(false);
                wasExtracted = true;
            }

            var pythonExecutablePath = FindRuntimeExecutablePath(runtimeRootPath)
                ?? throw new InvalidOperationException("Demucs 运行时目录中缺少 python.exe。");

            return CacheResolution(
                new DemucsRuntimeResolution
                {
                    PythonExecutablePath = pythonExecutablePath,
                    RuntimeRootPath = runtimeRootPath,
                    ModelRepositoryPath = modelRepositoryPath,
                    WasExtracted = wasExtracted
                });
        }
        finally
        {
            _syncLock.Release();
        }
    }

    private bool TryGetCachedResolution(out DemucsRuntimeResolution resolution)
    {
        if (_cachedResolution is not null &&
            File.Exists(_cachedResolution.PythonExecutablePath) &&
            IsValidModelRepository(_cachedResolution.ModelRepositoryPath))
        {
            resolution = _cachedResolution;
            return true;
        }

        resolution = null!;
        return false;
    }

    private DemucsRuntimeResolution CacheResolution(DemucsRuntimeResolution resolution)
    {
        _cachedResolution = resolution;
        return resolution;
    }

    private string FindAvailableRuntimeRootPath(string storageRootPath)
    {
        var bundledRuntimeRootPath = GetBundledRuntimeRootPath();
        if (!string.IsNullOrWhiteSpace(FindRuntimeExecutablePath(bundledRuntimeRootPath)))
        {
            return bundledRuntimeRootPath;
        }

        var extractedRuntimeRootPath = GetExtractedRuntimeRootPath(storageRootPath);
        return !string.IsNullOrWhiteSpace(FindRuntimeExecutablePath(extractedRuntimeRootPath))
            ? extractedRuntimeRootPath
            : string.Empty;
    }

    private string FindAvailableModelRepositoryPath(string storageRootPath)
    {
        var bundledModelRepositoryPath = FindModelRepositoryPath(GetBundledModelRootPath());
        if (!string.IsNullOrWhiteSpace(bundledModelRepositoryPath))
        {
            return bundledModelRepositoryPath;
        }

        var extractedModelRepositoryPath = FindModelRepositoryPath(GetExtractedModelRootPath(storageRootPath));
        return extractedModelRepositoryPath ?? string.Empty;
    }

    private async Task<string> ExtractRuntimeArchiveAsync(
        string archivePath,
        string storageRootPath,
        CancellationToken cancellationToken)
    {
        _logger.Log(LogLevel.Info, "正在解压 Demucs 离线运行时...");

        var stagingDirectoryPath = Path.Combine(storageRootPath, $".demucs-runtime-staging-{Guid.NewGuid():N}");
        Directory.CreateDirectory(stagingDirectoryPath);

        try
        {
            await Task.Run(() => ZipFile.ExtractToDirectory(archivePath, stagingDirectoryPath), cancellationToken)
                .ConfigureAwait(false);

            var extractedRuntimeRootPath = ResolveExtractedRuntimeRootPath(stagingDirectoryPath)
                ?? throw new InvalidOperationException("Demucs 运行时包中缺少 python.exe。");

            var targetRuntimeRootPath = GetExtractedRuntimeRootPath(storageRootPath);
            if (Directory.Exists(targetRuntimeRootPath))
            {
                Directory.Delete(targetRuntimeRootPath, recursive: true);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(targetRuntimeRootPath)!);
            Directory.Move(extractedRuntimeRootPath, targetRuntimeRootPath);

            _logger.Log(LogLevel.Info, "Demucs 离线运行时已准备完成。");
            return targetRuntimeRootPath;
        }
        catch
        {
            TryDeleteDirectory(GetExtractedRuntimeRootPath(storageRootPath));
            throw;
        }
        finally
        {
            TryDeleteDirectory(stagingDirectoryPath);
        }
    }

    private async Task<string> ExtractModelArchiveAsync(
        string archivePath,
        string storageRootPath,
        CancellationToken cancellationToken)
    {
        _logger.Log(LogLevel.Info, "正在解压 Demucs 模型仓...");

        var stagingDirectoryPath = Path.Combine(storageRootPath, $".demucs-model-staging-{Guid.NewGuid():N}");
        Directory.CreateDirectory(stagingDirectoryPath);

        try
        {
            await Task.Run(() => ZipFile.ExtractToDirectory(archivePath, stagingDirectoryPath), cancellationToken)
                .ConfigureAwait(false);

            var extractedModelRepositoryPath = FindModelRepositoryPath(stagingDirectoryPath)
                ?? throw new InvalidOperationException("Demucs 模型包缺少 htdemucs_ft 所需文件。");

            var targetModelRootPath = GetExtractedModelRootPath(storageRootPath);
            if (Directory.Exists(targetModelRootPath))
            {
                Directory.Delete(targetModelRootPath, recursive: true);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(targetModelRootPath)!);
            Directory.Move(extractedModelRepositoryPath, targetModelRootPath);

            _logger.Log(LogLevel.Info, "Demucs 模型仓已准备完成。");
            return targetModelRootPath;
        }
        catch
        {
            TryDeleteDirectory(GetExtractedModelRootPath(storageRootPath));
            throw;
        }
        finally
        {
            TryDeleteDirectory(stagingDirectoryPath);
        }
    }

    private string ResolveWritableStorageRootPath()
    {
        var applicationLocalRootPath = Path.Combine(
            AppContext.BaseDirectory,
            _configuration.RuntimeDirectoryName,
            _configuration.DemucsDirectoryName);

        if (CanWriteToDirectory(applicationLocalRootPath))
        {
            return applicationLocalRootPath;
        }

        var fallbackRootPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            _configuration.LocalDataDirectoryName,
            _configuration.RuntimeDirectoryName,
            _configuration.DemucsDirectoryName);

        Directory.CreateDirectory(fallbackRootPath);
        return fallbackRootPath;
    }

    private string GetBundledRuntimeRootPath() =>
        Path.Combine(
            AppContext.BaseDirectory,
            _configuration.RuntimeDirectoryName,
            _configuration.DemucsDirectoryName,
            _configuration.DemucsRuntimeDirectoryName);

    private string GetBundledModelRootPath() =>
        Path.Combine(
            AppContext.BaseDirectory,
            _configuration.RuntimeDirectoryName,
            _configuration.DemucsDirectoryName,
            _configuration.DemucsModelsDirectoryName);

    private string GetBundledRuntimeArchivePath() =>
        Path.Combine(
            AppContext.BaseDirectory,
            _configuration.RuntimeDirectoryName,
            _configuration.DemucsDirectoryName,
            _configuration.DemucsPackagesDirectoryName,
            _configuration.DemucsRuntimeArchiveFileName);

    private string GetBundledModelArchivePath() =>
        Path.Combine(
            AppContext.BaseDirectory,
            _configuration.RuntimeDirectoryName,
            _configuration.DemucsDirectoryName,
            _configuration.DemucsPackagesDirectoryName,
            _configuration.DemucsModelArchiveFileName);

    private string GetExtractedRuntimeRootPath(string storageRootPath) =>
        Path.Combine(storageRootPath, _configuration.DemucsRuntimeDirectoryName);

    private string GetExtractedModelRootPath(string storageRootPath) =>
        Path.Combine(storageRootPath, _configuration.DemucsModelsDirectoryName);

    private string? ResolveExtractedRuntimeRootPath(string rootDirectoryPath)
    {
        var pythonExecutablePath = FindRuntimeExecutablePath(rootDirectoryPath);
        return pythonExecutablePath is null
            ? null
            : Path.GetDirectoryName(pythonExecutablePath);
    }

    private string? FindRuntimeExecutablePath(string rootDirectoryPath)
    {
        if (!Directory.Exists(rootDirectoryPath))
        {
            return null;
        }

        return Directory.EnumerateFiles(
                rootDirectoryPath,
                _configuration.DemucsPythonExecutableFileName,
                SearchOption.AllDirectories)
            .FirstOrDefault();
    }

    private string? FindModelRepositoryPath(string rootDirectoryPath)
    {
        if (!Directory.Exists(rootDirectoryPath))
        {
            return null;
        }

        if (IsValidModelRepository(rootDirectoryPath))
        {
            return rootDirectoryPath;
        }

        var modelConfigFileName = $"{_configuration.DemucsModelName}.yaml";
        foreach (var configFilePath in Directory.EnumerateFiles(
                     rootDirectoryPath,
                     modelConfigFileName,
                     SearchOption.AllDirectories))
        {
            var candidateDirectoryPath = Path.GetDirectoryName(configFilePath);
            if (!string.IsNullOrWhiteSpace(candidateDirectoryPath) &&
                IsValidModelRepository(candidateDirectoryPath))
            {
                return candidateDirectoryPath;
            }
        }

        return null;
    }

    private bool IsValidModelRepository(string rootDirectoryPath)
    {
        if (!Directory.Exists(rootDirectoryPath))
        {
            return false;
        }

        return _configuration.DemucsRequiredModelFileNames.All(fileName =>
            File.Exists(Path.Combine(rootDirectoryPath, fileName)));
    }

    private static bool CanWriteToDirectory(string directoryPath)
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
