using System;
using System.Collections.Generic;
using System.IO;
using Vidvix.Core.Interfaces;
using Vidvix.Core.Models;

namespace Vidvix.Services;

public sealed class MediaImportDiscoveryService : IMediaImportDiscoveryService
{
    private readonly HashSet<string> _supportedInputExtensions;

    public MediaImportDiscoveryService(ApplicationConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        _supportedInputExtensions = new HashSet<string>(configuration.SupportedInputFileTypes, StringComparer.OrdinalIgnoreCase);
    }

    public MediaImportDiscoveryResult Discover(IEnumerable<string> inputPaths)
    {
        ArgumentNullException.ThrowIfNull(inputPaths);

        var supportedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var unsupportedEntries = 0;
        var missingEntries = 0;
        var unavailableDirectories = 0;

        foreach (var inputPath in inputPaths)
        {
            var path = Path.GetFullPath(inputPath);

            if (File.Exists(path))
            {
                AddFileIfSupported(path, supportedFiles, ref unsupportedEntries);
                continue;
            }

            if (Directory.Exists(path))
            {
                CollectDirectory(path, supportedFiles, ref unsupportedEntries, ref unavailableDirectories);
                continue;
            }

            missingEntries++;
        }

        return new MediaImportDiscoveryResult(
            supportedFiles,
            unsupportedEntries,
            missingEntries,
            unavailableDirectories);
    }

    private void CollectDirectory(
        string directoryPath,
        HashSet<string> supportedFiles,
        ref int unsupportedEntries,
        ref int unavailableDirectories)
    {
        try
        {
            foreach (var filePath in Directory.EnumerateFiles(directoryPath))
            {
                AddFileIfSupported(filePath, supportedFiles, ref unsupportedEntries);
            }

            foreach (var childDirectoryPath in Directory.EnumerateDirectories(directoryPath))
            {
                CollectDirectory(childDirectoryPath, supportedFiles, ref unsupportedEntries, ref unavailableDirectories);
            }
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
        {
            unavailableDirectories++;
        }
    }

    private void AddFileIfSupported(
        string filePath,
        HashSet<string> supportedFiles,
        ref int unsupportedEntries)
    {
        var extension = Path.GetExtension(filePath);

        if (string.IsNullOrWhiteSpace(extension) || !_supportedInputExtensions.Contains(extension))
        {
            unsupportedEntries++;
            return;
        }

        supportedFiles.Add(Path.GetFullPath(filePath));
    }
}
