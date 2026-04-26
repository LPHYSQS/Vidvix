using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Vidvix.Core.Interfaces;
using Vidvix.Core.Models;
using Vidvix.Utils;

namespace Vidvix.Services.AI;

internal sealed class AiPreparedModelCache
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> DirectoryLocks =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly ApplicationConfiguration _configuration;
    private readonly ILogger _logger;

    public AiPreparedModelCache(ApplicationConfiguration configuration, ILogger logger)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<string> EnsurePreparedModelDirectoryAsync(
        AiRuntimeDescriptor runtimeDescriptor,
        AiRuntimeModelDescriptor modelDescriptor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(runtimeDescriptor);
        ArgumentNullException.ThrowIfNull(modelDescriptor);

        var preparedDirectoryPath = MutableRuntimeStorage.GetLocalStorageRootPath(
            _configuration.LocalDataDirectoryName,
            _configuration.RuntimeDirectoryName,
            _configuration.AiRuntimeDirectoryName,
            _configuration.AiPreparedModelsDirectoryName,
            runtimeDescriptor.Id,
            modelDescriptor.Id,
            modelDescriptor.PreparedDirectoryName);

        Directory.CreateDirectory(preparedDirectoryPath);
        var directoryLock = DirectoryLocks.GetOrAdd(
            preparedDirectoryPath,
            static _ => new SemaphoreSlim(1, 1));
        await directoryLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            foreach (var asset in modelDescriptor.Assets)
            {
                await CopyIfNeededAsync(
                        asset.ConfigPath,
                        Path.Combine(preparedDirectoryPath, Path.GetFileName(asset.ConfigPath)),
                        cancellationToken)
                    .ConfigureAwait(false);
                await CopyIfNeededAsync(
                        asset.WeightPath,
                        Path.Combine(preparedDirectoryPath, Path.GetFileName(asset.WeightPath)),
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            return preparedDirectoryPath;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.Log(
                LogLevel.Warning,
                $"Failed to prepare AI model cache for {runtimeDescriptor.DisplayName}/{modelDescriptor.DisplayName}.",
                exception);
            throw;
        }
        finally
        {
            directoryLock.Release();
        }
    }

    private static async Task CopyIfNeededAsync(
        string sourcePath,
        string targetPath,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            throw new FileNotFoundException("Model asset source path is empty.", sourcePath);
        }

        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException("Model asset source file is missing.", sourcePath);
        }

        if (!NeedsCopy(sourcePath, targetPath))
        {
            return;
        }

        var targetDirectoryPath = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrWhiteSpace(targetDirectoryPath))
        {
            Directory.CreateDirectory(targetDirectoryPath);
        }

        await using var sourceStream = new FileStream(
            sourcePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 1024 * 128,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var targetStream = new FileStream(
            targetPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 1024 * 128,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await sourceStream.CopyToAsync(targetStream, cancellationToken).ConfigureAwait(false);
        File.SetLastWriteTimeUtc(targetPath, File.GetLastWriteTimeUtc(sourcePath));
    }

    private static bool NeedsCopy(string sourcePath, string targetPath)
    {
        if (!File.Exists(targetPath))
        {
            return true;
        }

        var sourceInfo = new FileInfo(sourcePath);
        var targetInfo = new FileInfo(targetPath);
        return sourceInfo.Length != targetInfo.Length ||
               sourceInfo.LastWriteTimeUtc != targetInfo.LastWriteTimeUtc;
    }
}
