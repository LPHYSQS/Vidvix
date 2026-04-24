using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Vidvix.Core.Models;

namespace Vidvix.Services.AI;

internal sealed class RealEsrganRuntimeParser
{
    private readonly ApplicationConfiguration _configuration;

    public RealEsrganRuntimeParser(ApplicationConfiguration configuration)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
    }

    public AiRuntimeDescriptor Parse(string packageRootPath, string licensesRootPath, string manifestsRootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageRootPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(licensesRootPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestsRootPath);

        var packageRelativePath = Path.Combine(
            _configuration.RuntimeDirectoryName,
            _configuration.AiRuntimeDirectoryName,
            _configuration.RealEsrganDirectoryName);
        var runtimeRootPath = Path.Combine(packageRootPath, _configuration.RealEsrganDirectoryName);
        var executableRelativePath = Path.Combine(packageRelativePath, "Bin", _configuration.RealEsrganExecutableFileName);
        var executablePath = Path.Combine(runtimeRootPath, "Bin", _configuration.RealEsrganExecutableFileName);
        var dependencyFilePaths = BuildDependencyPaths(runtimeRootPath);
        var dependencyRelativePaths = BuildDependencyRelativePaths(packageRelativePath);
        var manifestRelativePath = Path.Combine(
            _configuration.RuntimeDirectoryName,
            _configuration.AiRuntimeDirectoryName,
            _configuration.AiManifestsDirectoryName,
            _configuration.RealEsrganManifestFileName);
        var manifestPath = Path.Combine(manifestsRootPath, _configuration.RealEsrganManifestFileName);
        var licenseFilePaths = BuildLicensePaths(licensesRootPath);
        var licenseRelativePaths = BuildLicenseRelativePaths();
        var standardModelConfigRelativePath = Path.Combine(
            packageRelativePath,
            "Configs",
            _configuration.RealEsrganStandardModelConfigFileName);
        var standardModelWeightRelativePath = Path.Combine(
            packageRelativePath,
            "Models",
            _configuration.RealEsrganStandardModelWeightFileName);
        var standardModelConfigPath = Path.Combine(
            runtimeRootPath,
            "Configs",
            _configuration.RealEsrganStandardModelConfigFileName);
        var standardModelWeightPath = Path.Combine(
            runtimeRootPath,
            "Models",
            _configuration.RealEsrganStandardModelWeightFileName);
        var animeX2ConfigRelativePath = Path.Combine(
            packageRelativePath,
            "Configs",
            _configuration.RealEsrganAnimeX2ModelConfigFileName);
        var animeX2WeightRelativePath = Path.Combine(
            packageRelativePath,
            "Models",
            _configuration.RealEsrganAnimeX2ModelWeightFileName);
        var animeX2ConfigPath = Path.Combine(
            runtimeRootPath,
            "Configs",
            _configuration.RealEsrganAnimeX2ModelConfigFileName);
        var animeX2WeightPath = Path.Combine(
            runtimeRootPath,
            "Models",
            _configuration.RealEsrganAnimeX2ModelWeightFileName);
        var animeX4ConfigRelativePath = Path.Combine(
            packageRelativePath,
            "Configs",
            _configuration.RealEsrganAnimeX4ModelConfigFileName);
        var animeX4WeightRelativePath = Path.Combine(
            packageRelativePath,
            "Models",
            _configuration.RealEsrganAnimeX4ModelWeightFileName);
        var animeX4ConfigPath = Path.Combine(
            runtimeRootPath,
            "Configs",
            _configuration.RealEsrganAnimeX4ModelConfigFileName);
        var animeX4WeightPath = Path.Combine(
            runtimeRootPath,
            "Models",
            _configuration.RealEsrganAnimeX4ModelWeightFileName);
        var missingPaths = BuildMissingPaths(
            executableRelativePath,
            executablePath,
            dependencyRelativePaths,
            dependencyFilePaths,
            manifestRelativePath,
            manifestPath,
            licenseRelativePaths,
            licenseFilePaths,
            standardModelConfigRelativePath,
            standardModelConfigPath,
            standardModelWeightRelativePath,
            standardModelWeightPath,
            animeX2ConfigRelativePath,
            animeX2ConfigPath,
            animeX2WeightRelativePath,
            animeX2WeightPath,
            animeX4ConfigRelativePath,
            animeX4ConfigPath,
            animeX4WeightRelativePath,
            animeX4WeightPath);
        var hasAnyArtifacts =
            Directory.Exists(runtimeRootPath) ||
            File.Exists(manifestPath);
        var (releaseTag, releasePublishedAt) = ReadManifestMetadata(manifestPath);

        return new AiRuntimeDescriptor
        {
            Id = "realesrgan",
            DisplayName = "Real-ESRGAN NCNN Vulkan",
            RuntimeVersion = releaseTag,
            ReleasePublishedAt = releasePublishedAt,
            PackageRelativePath = packageRelativePath,
            RuntimeRootPath = runtimeRootPath,
            ExecutablePath = executablePath,
            ManifestPath = manifestPath,
            DependencyFilePaths = dependencyFilePaths,
            LicenseFilePaths = licenseFilePaths,
            Models = new[]
            {
                new AiRuntimeModelDescriptor
                {
                    Id = "standard",
                    DisplayName = "Standard",
                    RuntimeModelName = _configuration.RealEsrganStandardModelName,
                    Version = "x4",
                    PreparedDirectoryName = "models",
                    NativeScaleFactors = new[] { 4 },
                    Assets = new[]
                    {
                        new AiRuntimeModelAssetDescriptor
                        {
                            FileStem = _configuration.RealEsrganStandardModelName,
                            DisplayName = _configuration.RealEsrganStandardModelName,
                            NativeScale = 4,
                            ConfigPath = standardModelConfigPath,
                            WeightPath = standardModelWeightPath
                        }
                    }
                },
                new AiRuntimeModelDescriptor
                {
                    Id = "anime",
                    DisplayName = "Anime",
                    RuntimeModelName = _configuration.RealEsrganAnimeModelName,
                    Version = "x2/x4",
                    PreparedDirectoryName = "models",
                    NativeScaleFactors = new[] { 2, 4 },
                    Assets = new[]
                    {
                        new AiRuntimeModelAssetDescriptor
                        {
                            FileStem = Path.GetFileNameWithoutExtension(_configuration.RealEsrganAnimeX2ModelConfigFileName),
                            DisplayName = "Anime x2",
                            NativeScale = 2,
                            ConfigPath = animeX2ConfigPath,
                            WeightPath = animeX2WeightPath
                        },
                        new AiRuntimeModelAssetDescriptor
                        {
                            FileStem = Path.GetFileNameWithoutExtension(_configuration.RealEsrganAnimeX4ModelConfigFileName),
                            DisplayName = "Anime x4",
                            NativeScale = 4,
                            ConfigPath = animeX4ConfigPath,
                            WeightPath = animeX4WeightPath
                        }
                    }
                }
            },
            Availability = missingPaths.Count == 0
                ? AiRuntimeAvailability.Available
                : hasAnyArtifacts
                    ? AiRuntimeAvailability.Invalid
                    : AiRuntimeAvailability.Missing,
            AvailabilityReason = missingPaths.Count == 0
                ? string.Empty
                : string.Join(", ", missingPaths)
        };
    }

    private IReadOnlyList<string> BuildDependencyPaths(string runtimeRootPath)
    {
        var dependencyPaths = new List<string>(_configuration.RealEsrganSupportLibraryFileNames.Count);
        foreach (var fileName in _configuration.RealEsrganSupportLibraryFileNames)
        {
            dependencyPaths.Add(Path.Combine(runtimeRootPath, "Bin", fileName));
        }

        return dependencyPaths;
    }

    private IReadOnlyList<string> BuildDependencyRelativePaths(string packageRelativePath)
    {
        var dependencyRelativePaths = new List<string>(_configuration.RealEsrganSupportLibraryFileNames.Count);
        foreach (var fileName in _configuration.RealEsrganSupportLibraryFileNames)
        {
            dependencyRelativePaths.Add(Path.Combine(packageRelativePath, "Bin", fileName));
        }

        return dependencyRelativePaths;
    }

    private IReadOnlyList<string> BuildLicensePaths(string licensesRootPath)
    {
        var licensePaths = new List<string>(_configuration.RealEsrganLicenseFileNames.Count);
        foreach (var fileName in _configuration.RealEsrganLicenseFileNames)
        {
            licensePaths.Add(Path.Combine(licensesRootPath, fileName));
        }

        return licensePaths;
    }

    private IReadOnlyList<string> BuildLicenseRelativePaths()
    {
        var licenseRelativePaths = new List<string>(_configuration.RealEsrganLicenseFileNames.Count);
        foreach (var fileName in _configuration.RealEsrganLicenseFileNames)
        {
            licenseRelativePaths.Add(Path.Combine(
                _configuration.RuntimeDirectoryName,
                _configuration.AiRuntimeDirectoryName,
                _configuration.AiLicensesDirectoryName,
                fileName));
        }

        return licenseRelativePaths;
    }

    private static List<string> BuildMissingPaths(
        string executableRelativePath,
        string executablePath,
        IReadOnlyList<string> dependencyRelativePaths,
        IReadOnlyList<string> dependencyPaths,
        string manifestRelativePath,
        string manifestPath,
        IReadOnlyList<string> licenseRelativePaths,
        IReadOnlyList<string> licensePaths,
        string standardModelConfigRelativePath,
        string standardModelConfigPath,
        string standardModelWeightRelativePath,
        string standardModelWeightPath,
        string animeX2ConfigRelativePath,
        string animeX2ConfigPath,
        string animeX2WeightRelativePath,
        string animeX2WeightPath,
        string animeX4ConfigRelativePath,
        string animeX4ConfigPath,
        string animeX4WeightRelativePath,
        string animeX4WeightPath)
    {
        var missingPaths = new List<string>();

        if (!File.Exists(executablePath))
        {
            missingPaths.Add(executableRelativePath);
        }

        for (var index = 0; index < dependencyPaths.Count; index++)
        {
            if (!File.Exists(dependencyPaths[index]))
            {
                missingPaths.Add(dependencyRelativePaths[index]);
            }
        }

        if (!File.Exists(manifestPath))
        {
            missingPaths.Add(manifestRelativePath);
        }

        for (var index = 0; index < licensePaths.Count; index++)
        {
            if (!File.Exists(licensePaths[index]))
            {
                missingPaths.Add(licenseRelativePaths[index]);
            }
        }

        if (!File.Exists(standardModelConfigPath))
        {
            missingPaths.Add(standardModelConfigRelativePath);
        }

        if (!File.Exists(standardModelWeightPath))
        {
            missingPaths.Add(standardModelWeightRelativePath);
        }

        if (!File.Exists(animeX2ConfigPath))
        {
            missingPaths.Add(animeX2ConfigRelativePath);
        }

        if (!File.Exists(animeX2WeightPath))
        {
            missingPaths.Add(animeX2WeightRelativePath);
        }

        if (!File.Exists(animeX4ConfigPath))
        {
            missingPaths.Add(animeX4ConfigRelativePath);
        }

        if (!File.Exists(animeX4WeightPath))
        {
            missingPaths.Add(animeX4WeightRelativePath);
        }

        return missingPaths;
    }

    private static (string ReleaseTag, string ReleasePublishedAt) ReadManifestMetadata(string manifestPath)
    {
        if (!File.Exists(manifestPath))
        {
            return (string.Empty, string.Empty);
        }

        using var stream = File.OpenRead(manifestPath);
        using var document = JsonDocument.Parse(stream);
        var root = document.RootElement;
        var releaseTag = root.TryGetProperty("releaseTag", out var releaseTagElement)
            ? releaseTagElement.GetString() ?? string.Empty
            : string.Empty;
        var releasePublishedAt = root.TryGetProperty("releasePublishedAt", out var releasePublishedAtElement)
            ? releasePublishedAtElement.GetString() ?? string.Empty
            : string.Empty;
        return (releaseTag, releasePublishedAt);
    }
}
