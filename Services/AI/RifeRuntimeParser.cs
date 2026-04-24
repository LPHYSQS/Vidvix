using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Vidvix.Core.Models;

namespace Vidvix.Services.AI;

internal sealed class RifeRuntimeParser
{
    private readonly ApplicationConfiguration _configuration;

    public RifeRuntimeParser(ApplicationConfiguration configuration)
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
            _configuration.RifeDirectoryName);
        var runtimeRootPath = Path.Combine(packageRootPath, _configuration.RifeDirectoryName);
        var executableRelativePath = Path.Combine(packageRelativePath, "Bin", _configuration.RifeExecutableFileName);
        var executablePath = Path.Combine(runtimeRootPath, "Bin", _configuration.RifeExecutableFileName);
        var dependencyFilePaths = BuildDependencyPaths(runtimeRootPath);
        var dependencyRelativePaths = BuildDependencyRelativePaths(packageRelativePath);
        var modelConfigRelativePath = Path.Combine(
            packageRelativePath,
            "Configs",
            _configuration.RifeModelDirectoryName,
            _configuration.RifeModelConfigFileName);
        var modelWeightRelativePath = Path.Combine(
            packageRelativePath,
            "Models",
            _configuration.RifeModelDirectoryName,
            _configuration.RifeModelWeightFileName);
        var modelConfigPath = Path.Combine(
            runtimeRootPath,
            "Configs",
            _configuration.RifeModelDirectoryName,
            _configuration.RifeModelConfigFileName);
        var modelWeightPath = Path.Combine(
            runtimeRootPath,
            "Models",
            _configuration.RifeModelDirectoryName,
            _configuration.RifeModelWeightFileName);
        var manifestRelativePath = Path.Combine(
            _configuration.RuntimeDirectoryName,
            _configuration.AiRuntimeDirectoryName,
            _configuration.AiManifestsDirectoryName,
            _configuration.RifeManifestFileName);
        var manifestPath = Path.Combine(manifestsRootPath, _configuration.RifeManifestFileName);
        var licenseFilePaths = BuildLicensePaths(licensesRootPath);
        var licenseRelativePaths = BuildLicenseRelativePaths();
        var missingPaths = BuildMissingPaths(
            executableRelativePath,
            executablePath,
            dependencyRelativePaths,
            dependencyFilePaths,
            modelConfigRelativePath,
            modelConfigPath,
            modelWeightRelativePath,
            modelWeightPath,
            manifestRelativePath,
            manifestPath,
            licenseRelativePaths,
            licenseFilePaths);
        var hasAnyArtifacts =
            Directory.Exists(runtimeRootPath) ||
            File.Exists(manifestPath);
        var (releaseTag, releasePublishedAt) = ReadManifestMetadata(manifestPath);

        return new AiRuntimeDescriptor
        {
            Id = "rife",
            DisplayName = "RIFE NCNN Vulkan",
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
                    Id = _configuration.RifeModelDirectoryName,
                    DisplayName = "RIFE v4.6",
                    RuntimeModelName = _configuration.RifeModelDirectoryName,
                    Version = "v4.6",
                    PreparedDirectoryName = _configuration.RifeModelDirectoryName,
                    Assets = new[]
                    {
                        new AiRuntimeModelAssetDescriptor
                        {
                            FileStem = "flownet",
                            DisplayName = "flownet",
                            ConfigPath = modelConfigPath,
                            WeightPath = modelWeightPath
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
        var dependencyPaths = new List<string>(_configuration.RifeSupportLibraryFileNames.Count);
        foreach (var fileName in _configuration.RifeSupportLibraryFileNames)
        {
            dependencyPaths.Add(Path.Combine(runtimeRootPath, "Bin", fileName));
        }

        return dependencyPaths;
    }

    private IReadOnlyList<string> BuildDependencyRelativePaths(string packageRelativePath)
    {
        var dependencyRelativePaths = new List<string>(_configuration.RifeSupportLibraryFileNames.Count);
        foreach (var fileName in _configuration.RifeSupportLibraryFileNames)
        {
            dependencyRelativePaths.Add(Path.Combine(packageRelativePath, "Bin", fileName));
        }

        return dependencyRelativePaths;
    }

    private IReadOnlyList<string> BuildLicensePaths(string licensesRootPath)
    {
        var licensePaths = new List<string>(_configuration.RifeLicenseFileNames.Count);
        foreach (var fileName in _configuration.RifeLicenseFileNames)
        {
            licensePaths.Add(Path.Combine(licensesRootPath, fileName));
        }

        return licensePaths;
    }

    private IReadOnlyList<string> BuildLicenseRelativePaths()
    {
        var licenseRelativePaths = new List<string>(_configuration.RifeLicenseFileNames.Count);
        foreach (var fileName in _configuration.RifeLicenseFileNames)
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
        string modelConfigRelativePath,
        string modelConfigPath,
        string modelWeightRelativePath,
        string modelWeightPath,
        string manifestRelativePath,
        string manifestPath,
        IReadOnlyList<string> licenseRelativePaths,
        IReadOnlyList<string> licensePaths)
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

        if (!File.Exists(modelConfigPath))
        {
            missingPaths.Add(modelConfigRelativePath);
        }

        if (!File.Exists(modelWeightPath))
        {
            missingPaths.Add(modelWeightRelativePath);
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
