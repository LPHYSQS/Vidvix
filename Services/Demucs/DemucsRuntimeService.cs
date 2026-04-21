using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Vidvix.Core.Interfaces;
using Vidvix.Core.Models;
using Vidvix.Utils;

namespace Vidvix.Services.Demucs;

public sealed class DemucsRuntimeService : IDemucsRuntimeService
{
    private readonly ApplicationConfiguration _configuration;
    private readonly ILocalizationService _localizationService;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _syncLock = new(1, 1);
    private readonly Dictionary<DemucsRuntimeVariant, DemucsRuntimeResolution> _cachedResolutions = new();

    public DemucsRuntimeService(
        ApplicationConfiguration configuration,
        ILocalizationService localizationService,
        ILogger logger)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _localizationService = localizationService ?? throw new ArgumentNullException(nameof(localizationService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<DemucsRuntimeResolution> EnsureAvailableAsync(
        CancellationToken cancellationToken = default,
        DemucsRuntimeVariant runtimeVariant = DemucsRuntimeVariant.Cpu)
    {
        if (TryGetCachedResolution(runtimeVariant, out var cachedResolution))
        {
            return cachedResolution;
        }

        await _syncLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (TryGetCachedResolution(runtimeVariant, out cachedResolution))
            {
                return cachedResolution;
            }

            var storageRootPath = ResolveWritableStorageRootPath();
            Directory.CreateDirectory(storageRootPath);

            var runtimeRootPath = FindAvailableRuntimeRootPath(storageRootPath, runtimeVariant);
            var modelRepositoryPath = FindAvailableModelRepositoryPath(storageRootPath);
            var wasExtracted = false;

            if (string.IsNullOrWhiteSpace(runtimeRootPath))
            {
                var runtimeArchivePath = GetBundledRuntimeArchivePath(runtimeVariant);
                if (!File.Exists(runtimeArchivePath))
                {
                    throw CreateLocalizedInvalidOperationException(
                        "splitAudio.runtime.missingRuntimePackage",
                        "未找到离线 Demucs {runtimeVariant} 运行时包，请补齐 {packagePath}。",
                        ("runtimeVariant", GetRuntimeVariantDisplayName(runtimeVariant)),
                        ("packagePath", Path.Combine(
                            "Tools",
                            _configuration.DemucsDirectoryName,
                            _configuration.DemucsPackagesDirectoryName,
                            GetRuntimeArchiveFileName(runtimeVariant))));
                }

                runtimeRootPath = await ExtractRuntimeArchiveAsync(
                    runtimeArchivePath,
                    storageRootPath,
                    runtimeVariant,
                    cancellationToken).ConfigureAwait(false);
                wasExtracted = true;
            }

            if (string.IsNullOrWhiteSpace(modelRepositoryPath))
            {
                var modelArchivePath = GetBundledModelArchivePath();
                if (!File.Exists(modelArchivePath))
                {
                    throw CreateLocalizedInvalidOperationException(
                        "splitAudio.runtime.missingModelRepository",
                        "未找到 Demucs 模型仓，请补齐 {modelPath} 或 {archivePath}。",
                        ("modelPath", Path.Combine("Tools", _configuration.DemucsDirectoryName, _configuration.DemucsModelsDirectoryName)),
                        ("archivePath", Path.Combine(
                            "Tools",
                            _configuration.DemucsDirectoryName,
                            _configuration.DemucsPackagesDirectoryName,
                            _configuration.DemucsModelArchiveFileName)));
                }

                modelRepositoryPath = await ExtractModelArchiveAsync(
                    modelArchivePath,
                    storageRootPath,
                    cancellationToken).ConfigureAwait(false);
                wasExtracted = true;
            }

            var pythonExecutablePath = FindRuntimeExecutablePath(runtimeRootPath)
                ?? throw CreateLocalizedInvalidOperationException(
                    "splitAudio.runtime.missingPythonExecutable",
                    "Demucs 运行时目录中缺少 python.exe。");

            return CacheResolution(
                runtimeVariant,
                new DemucsRuntimeResolution
                {
                    RuntimeVariant = runtimeVariant,
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

    private bool TryGetCachedResolution(DemucsRuntimeVariant runtimeVariant, out DemucsRuntimeResolution resolution)
    {
        if (_cachedResolutions.TryGetValue(runtimeVariant, out var cachedResolution) &&
            File.Exists(cachedResolution.PythonExecutablePath) &&
            IsValidModelRepository(cachedResolution.ModelRepositoryPath))
        {
            resolution = cachedResolution;
            return true;
        }

        resolution = null!;
        return false;
    }

    private DemucsRuntimeResolution CacheResolution(
        DemucsRuntimeVariant runtimeVariant,
        DemucsRuntimeResolution resolution)
    {
        _cachedResolutions[runtimeVariant] = resolution;
        return resolution;
    }

    private string FindAvailableRuntimeRootPath(string storageRootPath, DemucsRuntimeVariant runtimeVariant)
    {
        var bundledRuntimeRootPath = GetBundledRuntimeRootPath(runtimeVariant);
        if (!string.IsNullOrWhiteSpace(FindRuntimeExecutablePath(bundledRuntimeRootPath)))
        {
            return bundledRuntimeRootPath;
        }

        var extractedRuntimeRootPath = GetExtractedRuntimeRootPath(storageRootPath, runtimeVariant);
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
        DemucsRuntimeVariant runtimeVariant,
        CancellationToken cancellationToken)
    {
        _logger.Log(LogLevel.Info, $"正在解压 Demucs {GetRuntimeVariantDisplayName(runtimeVariant)} 离线运行时...");

        var stagingDirectoryPath = Path.Combine(storageRootPath, $".demucs-runtime-staging-{Guid.NewGuid():N}");
        Directory.CreateDirectory(stagingDirectoryPath);

        try
        {
            await Task.Run(() => ZipFile.ExtractToDirectory(archivePath, stagingDirectoryPath), cancellationToken)
                .ConfigureAwait(false);

            var extractedRuntimeRootPath = ResolveExtractedRuntimeRootPath(stagingDirectoryPath)
                ?? throw CreateLocalizedInvalidOperationException(
                    "splitAudio.runtime.invalidRuntimePackage",
                    "Demucs 运行时包中缺少 python.exe。");

            var targetRuntimeRootPath = GetExtractedRuntimeRootPath(storageRootPath, runtimeVariant);
            if (Directory.Exists(targetRuntimeRootPath))
            {
                Directory.Delete(targetRuntimeRootPath, recursive: true);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(targetRuntimeRootPath)!);
            Directory.Move(extractedRuntimeRootPath, targetRuntimeRootPath);

            _logger.Log(LogLevel.Info, $"Demucs {GetRuntimeVariantDisplayName(runtimeVariant)} 离线运行时已准备完成。");
            return targetRuntimeRootPath;
        }
        catch
        {
            TryDeleteDirectory(GetExtractedRuntimeRootPath(storageRootPath, runtimeVariant));
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
                ?? throw CreateLocalizedInvalidOperationException(
                    "splitAudio.runtime.invalidModelPackage",
                    "Demucs 模型包缺少 htdemucs_ft 所需文件。");

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
            ApplicationPaths.ExecutableDirectoryPath,
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

    private string GetBundledRuntimeRootPath(DemucsRuntimeVariant runtimeVariant) =>
        Path.Combine(
            ApplicationPaths.ExecutableDirectoryPath,
            _configuration.RuntimeDirectoryName,
            _configuration.DemucsDirectoryName,
            GetRuntimeDirectoryName(runtimeVariant));

    private string GetBundledModelRootPath() =>
        Path.Combine(
            ApplicationPaths.ExecutableDirectoryPath,
            _configuration.RuntimeDirectoryName,
            _configuration.DemucsDirectoryName,
            _configuration.DemucsModelsDirectoryName);

    private string GetBundledRuntimeArchivePath(DemucsRuntimeVariant runtimeVariant) =>
        Path.Combine(
            ApplicationPaths.ExecutableDirectoryPath,
            _configuration.RuntimeDirectoryName,
            _configuration.DemucsDirectoryName,
            _configuration.DemucsPackagesDirectoryName,
            GetRuntimeArchiveFileName(runtimeVariant));

    private string GetBundledModelArchivePath() =>
        Path.Combine(
            ApplicationPaths.ExecutableDirectoryPath,
            _configuration.RuntimeDirectoryName,
            _configuration.DemucsDirectoryName,
            _configuration.DemucsPackagesDirectoryName,
            _configuration.DemucsModelArchiveFileName);

    private string GetExtractedRuntimeRootPath(string storageRootPath, DemucsRuntimeVariant runtimeVariant) =>
        Path.Combine(storageRootPath, GetRuntimeDirectoryName(runtimeVariant));

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

    private string GetRuntimeDirectoryName(DemucsRuntimeVariant runtimeVariant) =>
        runtimeVariant switch
        {
            DemucsRuntimeVariant.Cuda => _configuration.DemucsCudaRuntimeDirectoryName,
            DemucsRuntimeVariant.DirectMl => _configuration.DemucsGpuRuntimeDirectoryName,
            _ => _configuration.DemucsRuntimeDirectoryName
        };

    private string GetRuntimeArchiveFileName(DemucsRuntimeVariant runtimeVariant) =>
        runtimeVariant switch
        {
            DemucsRuntimeVariant.Cuda => _configuration.DemucsCudaRuntimeArchiveFileName,
            DemucsRuntimeVariant.DirectMl => _configuration.DemucsGpuRuntimeArchiveFileName,
            _ => _configuration.DemucsRuntimeArchiveFileName
        };

    private static string GetRuntimeVariantDisplayName(DemucsRuntimeVariant runtimeVariant) =>
        runtimeVariant switch
        {
            DemucsRuntimeVariant.Cuda => "CUDA",
            DemucsRuntimeVariant.DirectMl => "DirectML",
            _ => "CPU"
        };

    private string GetLocalizedText(string key, string fallback) =>
        _localizationService.GetString(key, fallback);

    private LocalizedInvalidOperationException CreateLocalizedInvalidOperationException(
        string key,
        string fallback,
        params (string Name, object? Value)[] arguments) =>
        new(() => FormatLocalizedText(key, fallback, arguments));

    private string FormatLocalizedText(
        string key,
        string fallback,
        params (string Name, object? Value)[] arguments)
    {
        if (arguments.Length == 0)
        {
            return GetLocalizedText(key, fallback);
        }

        var localizedArguments = arguments.ToDictionary(
            argument => argument.Name,
            argument => argument.Value,
            StringComparer.Ordinal);
        return _localizationService.Format(key, localizedArguments, fallback);
    }
}
