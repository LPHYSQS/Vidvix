using System;
using System.Collections.Generic;
using System.IO;
using Vidvix.Core.Interfaces;
using Vidvix.Core.Models;

namespace Vidvix.Services;

public sealed class MediaImportDiscoveryService : IMediaImportDiscoveryService
{
    public MediaImportDiscoveryResult Discover(IEnumerable<string> inputPaths, IEnumerable<string> supportedInputFileTypes)
    {
        ArgumentNullException.ThrowIfNull(inputPaths);
        ArgumentNullException.ThrowIfNull(supportedInputFileTypes);

        var supportedInputExtensions = new HashSet<string>(supportedInputFileTypes, StringComparer.OrdinalIgnoreCase);

        var supportedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var unsupportedEntries = 0;
        var missingEntries = 0;
        var unavailableDirectories = 0;

        foreach (var inputPath in inputPaths)
        {
            var path = Path.GetFullPath(inputPath);

            if (File.Exists(path))
            {
                AddFileIfSupported(path, supportedInputExtensions, supportedFiles, ref unsupportedEntries);
                continue;
            }

            if (Directory.Exists(path))
            {
                CollectDirectory(path, supportedInputExtensions, supportedFiles, ref unsupportedEntries, ref unavailableDirectories);
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
        HashSet<string> supportedInputExtensions,
        HashSet<string> supportedFiles,
        ref int unsupportedEntries,
        ref int unavailableDirectories)
    {
        try
        {
            foreach (var filePath in Directory.EnumerateFiles(directoryPath))
            {
                AddFileIfSupported(filePath, supportedInputExtensions, supportedFiles, ref unsupportedEntries);
            }

            foreach (var childDirectoryPath in Directory.EnumerateDirectories(directoryPath))
            {
                CollectDirectory(childDirectoryPath, supportedInputExtensions, supportedFiles, ref unsupportedEntries, ref unavailableDirectories);
            }
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
        {
            unavailableDirectories++;
        }
    }

    private void AddFileIfSupported(
        string filePath,
        HashSet<string> supportedInputExtensions,
        HashSet<string> supportedFiles,
        ref int unsupportedEntries)
    {
        var extension = Path.GetExtension(filePath);

        if (string.IsNullOrWhiteSpace(extension) || !supportedInputExtensions.Contains(extension))
        {
            unsupportedEntries++;
            return;
        }

        supportedFiles.Add(Path.GetFullPath(filePath));
    }
}
