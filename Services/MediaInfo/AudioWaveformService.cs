using System;
using System.Collections.Concurrent;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Vidvix.Core.Interfaces;
using Vidvix.Core.Models;

namespace Vidvix.Services.MediaInfo;

public sealed class AudioWaveformService : IAudioWaveformService
{
    private const int WaveformWidth = 1600;
    private const int WaveformHeight = 360;

    private readonly IFFmpegRuntimeService _ffmpegRuntimeService;
    private readonly IFFmpegService _ffmpegService;
    private readonly ApplicationConfiguration _configuration;
    private readonly ILogger _logger;
    private readonly ConcurrentDictionary<string, Uri> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Task<Uri?>> _inFlightRequests = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _generationLimiter = new(1, 1);

    public AudioWaveformService(
        IFFmpegRuntimeService ffmpegRuntimeService,
        IFFmpegService ffmpegService,
        ApplicationConfiguration configuration,
        ILogger logger)
    {
        _ffmpegRuntimeService = ffmpegRuntimeService ?? throw new ArgumentNullException(nameof(ffmpegRuntimeService));
        _ffmpegService = ffmpegService ?? throw new ArgumentNullException(nameof(ffmpegService));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<Uri?> GetWaveformImageUriAsync(string inputPath, CancellationToken cancellationToken = default)
    {
        if (!TryCreateCacheContext(inputPath, out var cacheContext))
        {
            return null;
        }

        if (TryGetCachedWaveform(cacheContext.CacheKey, out var cachedUri))
        {
            return cachedUri;
        }

        var loadTask = _inFlightRequests.GetOrAdd(
            cacheContext.CacheKey,
            _ => GenerateWaveformCoreAsync(cacheContext));

        try
        {
            return await loadTask.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (loadTask.IsCompleted)
            {
                _inFlightRequests.TryRemove(cacheContext.CacheKey, out _);
            }
        }
    }

    private bool TryGetCachedWaveform(string cacheKey, out Uri waveformUri)
    {
        if (_cache.TryGetValue(cacheKey, out var cachedUri) &&
            cachedUri.IsFile &&
            IsUsableWaveformFile(cachedUri.LocalPath))
        {
            waveformUri = cachedUri;
            return true;
        }

        _cache.TryRemove(cacheKey, out _);
        waveformUri = null!;
        return false;
    }

    private async Task<Uri?> GenerateWaveformCoreAsync(WaveformCacheContext cacheContext)
    {
        try
        {
            var runtimeResolution = await _ffmpegRuntimeService.EnsureAvailableAsync().ConfigureAwait(false);
            if (!File.Exists(runtimeResolution.ExecutablePath))
            {
                return null;
            }

            if (IsUsableWaveformFile(cacheContext.OutputPath))
            {
                return CacheWaveform(cacheContext.CacheKey, cacheContext.OutputPath);
            }

            await _generationLimiter.WaitAsync().ConfigureAwait(false);
            try
            {
                if (IsUsableWaveformFile(cacheContext.OutputPath))
                {
                    return CacheWaveform(cacheContext.CacheKey, cacheContext.OutputPath);
                }

                var outputDirectory = Path.GetDirectoryName(cacheContext.OutputPath)
                    ?? throw new InvalidOperationException("音频波形缓存目录不可用。");

                Directory.CreateDirectory(outputDirectory);

                var temporaryPath = cacheContext.OutputPath + ".tmp.png";
                TryDeleteFile(temporaryPath);

                var result = await _ffmpegService.ExecuteAsync(
                    CreateWaveformCommand(runtimeResolution.ExecutablePath, cacheContext.InputPath, temporaryPath),
                    new FFmpegExecutionOptions
                    {
                        Timeout = TimeSpan.FromSeconds(45)
                    }).ConfigureAwait(false);

                if (!result.WasSuccessful || !IsUsableWaveformFile(temporaryPath))
                {
                    TryDeleteFile(temporaryPath);
                    if (!string.IsNullOrWhiteSpace(result.FailureReason))
                    {
                        _logger.Log(LogLevel.Warning, $"生成音频波形失败：{result.FailureReason}");
                    }

                    return null;
                }

                File.Move(temporaryPath, cacheContext.OutputPath, overwrite: true);
                return CacheWaveform(cacheContext.CacheKey, cacheContext.OutputPath);
            }
            finally
            {
                _generationLimiter.Release();
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.Log(LogLevel.Warning, $"生成音频波形时发生异常：{cacheContext.InputPath}", exception);
            return null;
        }
    }

    private FFmpegCommand CreateWaveformCommand(string ffmpegPath, string inputPath, string outputPath) =>
        new(
            ffmpegPath,
            new[]
            {
                "-hide_banner",
                "-loglevel", "error",
                _configuration.OverwriteOutputFiles ? "-y" : "-n",
                "-i", inputPath,
                "-vn",
                "-filter_complex", $"aformat=channel_layouts=mono,showwavespic=s={WaveformWidth}x{WaveformHeight}:colors=0x80D8FF",
                "-frames:v", "1",
                outputPath
            });

    private Uri CacheWaveform(string cacheKey, string outputPath)
    {
        var waveformUri = new Uri(outputPath);
        _cache[cacheKey] = waveformUri;
        return waveformUri;
    }

    private bool TryCreateCacheContext(string inputPath, out WaveformCacheContext cacheContext)
    {
        cacheContext = default;

        if (string.IsNullOrWhiteSpace(inputPath))
        {
            return false;
        }

        var fullPath = Path.GetFullPath(inputPath);
        if (!File.Exists(fullPath))
        {
            return false;
        }

        var fileInfo = new FileInfo(fullPath);
        var cacheKey = ComputeCacheKey(fullPath, fileInfo.Length, fileInfo.LastWriteTimeUtc.Ticks);
        var outputPath = Path.Combine(
            GetCacheRootDirectory(),
            cacheKey[..2],
            cacheKey + ".png");

        cacheContext = new WaveformCacheContext(fullPath, cacheKey, outputPath);
        return true;
    }

    private string GetCacheRootDirectory() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            _configuration.LocalDataDirectoryName,
            _configuration.AudioWaveformCacheDirectoryName);

    private static string ComputeCacheKey(string inputPath, long fileLength, long lastWriteTimeTicks)
    {
        using var sha256 = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes($"{inputPath}|{fileLength}|{lastWriteTimeTicks}");
        return Convert.ToHexString(sha256.ComputeHash(bytes));
    }

    private static bool IsUsableWaveformFile(string path) =>
        File.Exists(path) &&
        new FileInfo(path).Length > 0;

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }

    private readonly record struct WaveformCacheContext(string InputPath, string CacheKey, string OutputPath);
}
