using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Vidvix.Core.Interfaces;
using Vidvix.Core.Models;

namespace Vidvix.Services.MediaInfo;

public sealed class VideoThumbnailService : IVideoThumbnailService
{
    private const double ShortClipSeekFactor = 0.25d;
    private const double StandardSeekFactor = 0.12d;
    private const double MinimumSeekSeconds = 0.75d;
    private const double MaximumSeekSeconds = 30d;
    private const double SeekSafetyTailSeconds = 0.5d;
    private const int RepresentativeFrameBatchSize = 90;
    private const int ThumbnailDecodeWidth = 320;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IFFmpegRuntimeService _ffmpegRuntimeService;
    private readonly IFFmpegService _ffmpegService;
    private readonly ApplicationConfiguration _configuration;
    private readonly ILogger _logger;
    private readonly ConcurrentDictionary<string, Uri> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Task<Uri?>> _inFlightRequests = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _generationLimiter = new(2, 2);

    public VideoThumbnailService(
        IFFmpegRuntimeService ffmpegRuntimeService,
        IFFmpegService ffmpegService,
        ApplicationConfiguration configuration,
        ILogger logger)
    {
        _ffmpegRuntimeService = ffmpegRuntimeService;
        _ffmpegService = ffmpegService;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<Uri?> GetThumbnailUriAsync(string inputPath, CancellationToken cancellationToken = default)
    {
        if (!TryCreateCacheContext(inputPath, out var cacheContext))
        {
            return null;
        }

        if (TryGetCachedThumbnail(cacheContext.CacheKey, out var cachedThumbnailUri))
        {
            return cachedThumbnailUri;
        }

        var loadTask = _inFlightRequests.GetOrAdd(
            cacheContext.CacheKey,
            _ => GenerateThumbnailCoreAsync(cacheContext));

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

    private bool TryGetCachedThumbnail(string cacheKey, out Uri thumbnailUri)
    {
        if (_cache.TryGetValue(cacheKey, out var cachedThumbnailUri) &&
            cachedThumbnailUri.IsFile &&
            IsUsableThumbnailFile(cachedThumbnailUri.LocalPath))
        {
            thumbnailUri = cachedThumbnailUri;
            return true;
        }

        _cache.TryRemove(cacheKey, out _);
        thumbnailUri = null!;
        return false;
    }

    private async Task<Uri?> GenerateThumbnailCoreAsync(ThumbnailCacheContext cacheContext)
    {
        try
        {
            var runtimeResolution = await _ffmpegRuntimeService.EnsureAvailableAsync().ConfigureAwait(false);
            var ffmpegPath = runtimeResolution.ExecutablePath;
            var ffprobePath = ResolveFfprobePath(ffmpegPath);
            if (!File.Exists(ffmpegPath) || !File.Exists(ffprobePath))
            {
                return null;
            }

            var probeInfo = await ProbeVideoInfoAsync(ffprobePath, cacheContext.InputPath).ConfigureAwait(false);
            if (!probeInfo.HasVideoStream)
            {
                return null;
            }

            var outputPath = GetThumbnailOutputPath(cacheContext);
            if (IsUsableThumbnailFile(outputPath))
            {
                return CacheThumbnail(cacheContext.CacheKey, outputPath);
            }

            await _generationLimiter.WaitAsync().ConfigureAwait(false);

            try
            {
                if (IsUsableThumbnailFile(outputPath))
                {
                    return CacheThumbnail(cacheContext.CacheKey, outputPath);
                }

                var generatedThumbnailPath = await GenerateThumbnailFileAsync(
                    ffmpegPath,
                    cacheContext,
                    outputPath,
                    probeInfo.DurationSeconds).ConfigureAwait(false);

                return generatedThumbnailPath is null
                    ? null
                    : CacheThumbnail(cacheContext.CacheKey, generatedThumbnailPath);
            }
            finally
            {
                _generationLimiter.Release();
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.Log(LogLevel.Warning, $"\u751f\u6210\u89c6\u9891\u7f29\u7565\u56fe\u65f6\u53d1\u751f\u5f02\u5e38\uff1a{cacheContext.InputPath}", exception);
            return null;
        }
    }

    private async Task<VideoProbeInfo> ProbeVideoInfoAsync(string ffprobePath, string inputPath)
    {
        var command = new FFmpegCommand(
            ffprobePath,
            new[]
            {
                "-v", "quiet",
                "-print_format", "json",
                "-show_entries", "format=duration:stream=codec_type",
                "-show_format",
                "-show_streams",
                "-select_streams", "v:0",
                inputPath
            });

        var result = await _ffmpegService.ExecuteAsync(
            command,
            new FFmpegExecutionOptions
            {
                Timeout = TimeSpan.FromSeconds(10)
            }).ConfigureAwait(false);

        if (!result.WasSuccessful || string.IsNullOrWhiteSpace(result.StandardOutput))
        {
            return VideoProbeInfo.Empty;
        }

        var response = JsonSerializer.Deserialize<ThumbnailProbeResponse>(result.StandardOutput, JsonOptions);
        var hasVideoStream = response?.streams?.Any(stream => string.Equals(stream.codec_type, "video", StringComparison.OrdinalIgnoreCase)) == true;
        var durationSeconds = ParseDurationSeconds(response?.format?.duration);
        return new VideoProbeInfo(hasVideoStream, durationSeconds);
    }

    private async Task<string?> GenerateThumbnailFileAsync(
        string ffmpegPath,
        ThumbnailCacheContext cacheContext,
        string outputPath,
        double? durationSeconds)
    {
        var cacheDirectoryPath = Path.GetDirectoryName(outputPath)
            ?? throw new InvalidOperationException("\u7f29\u7565\u56fe\u7f13\u5b58\u76ee\u5f55\u65e0\u6548\u3002");

        Directory.CreateDirectory(cacheDirectoryPath);

        var temporaryPath = outputPath + ".tmp.jpg";
        TryDeleteFile(temporaryPath);

        var representativeSeekSeconds = CalculateSeekSeconds(durationSeconds);
        if (await TryCreateThumbnailAsync(
                CreateRepresentativeThumbnailCommand(ffmpegPath, cacheContext.InputPath, representativeSeekSeconds, temporaryPath),
                temporaryPath).ConfigureAwait(false))
        {
            FinalizeGeneratedThumbnail(cacheContext, temporaryPath, outputPath);
            return outputPath;
        }

        TryDeleteFile(temporaryPath);

        if (await TryCreateThumbnailAsync(
                CreateFallbackThumbnailCommand(ffmpegPath, cacheContext.InputPath, temporaryPath),
                temporaryPath).ConfigureAwait(false))
        {
            FinalizeGeneratedThumbnail(cacheContext, temporaryPath, outputPath);
            return outputPath;
        }

        TryDeleteFile(temporaryPath);
        return null;
    }

    private async Task<bool> TryCreateThumbnailAsync(FFmpegCommand command, string outputPath)
    {
        var result = await _ffmpegService.ExecuteAsync(
            command,
            new FFmpegExecutionOptions
            {
                Timeout = TimeSpan.FromSeconds(20)
            }).ConfigureAwait(false);

        if (result.WasSuccessful && IsUsableThumbnailFile(outputPath))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(result.FailureReason))
        {
            _logger.Log(LogLevel.Warning, $"\u7f29\u7565\u56fe\u751f\u6210\u5c1d\u8bd5\u5931\u8d25\uff1a{result.FailureReason}");
        }

        return false;
    }

    private void FinalizeGeneratedThumbnail(ThumbnailCacheContext cacheContext, string temporaryPath, string outputPath)
    {
        File.Move(temporaryPath, outputPath, overwrite: true);
        DeleteStaleThumbnailFiles(cacheContext, outputPath);
    }

    private FFmpegCommand CreateRepresentativeThumbnailCommand(
        string ffmpegPath,
        string inputPath,
        double seekSeconds,
        string outputPath)
    {
        var arguments = new[]
        {
            "-hide_banner",
            "-loglevel", "error",
            "-ss", seekSeconds.ToString("0.###", CultureInfo.InvariantCulture),
            "-i", inputPath,
            "-map", "0:v:0",
            "-frames:v", "1",
            "-an",
            "-sn",
            "-dn",
            "-vf", $"thumbnail={RepresentativeFrameBatchSize},scale={ThumbnailDecodeWidth}:-2:force_original_aspect_ratio=decrease",
            "-q:v", "4",
            "-y",
            outputPath
        };

        return new FFmpegCommand(ffmpegPath, arguments);
    }

    private FFmpegCommand CreateFallbackThumbnailCommand(string ffmpegPath, string inputPath, string outputPath)
    {
        var arguments = new[]
        {
            "-hide_banner",
            "-loglevel", "error",
            "-i", inputPath,
            "-map", "0:v:0",
            "-frames:v", "1",
            "-an",
            "-sn",
            "-dn",
            "-vf", $"thumbnail=30,scale={ThumbnailDecodeWidth}:-2:force_original_aspect_ratio=decrease",
            "-q:v", "5",
            "-y",
            outputPath
        };

        return new FFmpegCommand(ffmpegPath, arguments);
    }

    private static double CalculateSeekSeconds(double? durationSeconds)
    {
        if (durationSeconds is not > 0)
        {
            return MinimumSeekSeconds;
        }

        var safeEndSeconds = Math.Max(0d, durationSeconds.Value - SeekSafetyTailSeconds);
        if (safeEndSeconds <= 0d)
        {
            return 0d;
        }

        var preferredFactor = durationSeconds.Value < 8d ? ShortClipSeekFactor : StandardSeekFactor;
        var preferredSeekSeconds = durationSeconds.Value * preferredFactor;
        var boundedSeekSeconds = Math.Clamp(preferredSeekSeconds, MinimumSeekSeconds, MaximumSeekSeconds);
        return Math.Min(boundedSeekSeconds, safeEndSeconds);
    }

    private Uri CacheThumbnail(string cacheKey, string outputPath)
    {
        var thumbnailUri = new Uri(outputPath, UriKind.Absolute);
        _cache[cacheKey] = thumbnailUri;
        return thumbnailUri;
    }

    private string GetThumbnailOutputPath(ThumbnailCacheContext cacheContext)
    {
        var cacheDirectoryPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            _configuration.LocalDataDirectoryName,
            _configuration.ThumbnailCacheDirectoryName);

        var cacheFileName = cacheContext.CacheFileNamePrefix + "_" + cacheContext.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture) + ".jpg";
        return Path.Combine(cacheDirectoryPath, cacheFileName);
    }

    private static bool IsUsableThumbnailFile(string thumbnailPath)
    {
        try
        {
            return File.Exists(thumbnailPath) && new FileInfo(thumbnailPath).Length > 0;
        }
        catch
        {
            return false;
        }
    }

    private void DeleteStaleThumbnailFiles(ThumbnailCacheContext cacheContext, string currentThumbnailPath)
    {
        var cacheDirectoryPath = Path.GetDirectoryName(currentThumbnailPath);
        if (string.IsNullOrWhiteSpace(cacheDirectoryPath) || !Directory.Exists(cacheDirectoryPath))
        {
            return;
        }

        foreach (var existingPath in Directory.EnumerateFiles(cacheDirectoryPath, cacheContext.CacheFileNamePrefix + "_*.jpg"))
        {
            if (string.Equals(existingPath, currentThumbnailPath, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            TryDeleteFile(existingPath);
        }
    }

    private static void TryDeleteFile(string filePath)
    {
        try
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
        catch
        {
        }
    }

    private string ResolveFfprobePath(string ffmpegExecutablePath)
    {
        var executableDirectory = Path.GetDirectoryName(ffmpegExecutablePath)
            ?? throw new InvalidOperationException("FFmpeg \u53ef\u6267\u884c\u6587\u4ef6\u8def\u5f84\u65e0\u6548\u3002");

        return Path.Combine(executableDirectory, _configuration.FFprobeExecutableFileName);
    }

    private static bool TryCreateCacheContext(string inputPath, out ThumbnailCacheContext cacheContext)
    {
        cacheContext = default;

        if (string.IsNullOrWhiteSpace(inputPath))
        {
            return false;
        }

        try
        {
            var fullPath = Path.GetFullPath(inputPath);
            if (!File.Exists(fullPath))
            {
                return false;
            }

            var fileInfo = new FileInfo(fullPath);
            var normalizedPath = fullPath.ToUpperInvariant();
            cacheContext = new ThumbnailCacheContext(
                fullPath,
                normalizedPath + "|" + fileInfo.LastWriteTimeUtc.Ticks,
                fileInfo.LastWriteTimeUtc,
                ComputeCacheFileNamePrefix(normalizedPath));
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string ComputeCacheFileNamePrefix(string normalizedPath)
    {
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalizedPath));
        return Convert.ToHexString(hashBytes[..10]);
    }

    private static double? ParseDurationSeconds(string? durationText)
    {
        return double.TryParse(durationText, NumberStyles.Float, CultureInfo.InvariantCulture, out var durationSeconds) &&
               durationSeconds > 0
            ? durationSeconds
            : null;
    }

    private readonly record struct ThumbnailCacheContext(
        string InputPath,
        string CacheKey,
        DateTime LastWriteTimeUtc,
        string CacheFileNamePrefix);

    private readonly record struct VideoProbeInfo(
        bool HasVideoStream,
        double? DurationSeconds)
    {
        public static VideoProbeInfo Empty => new(false, null);
    }

    private sealed class ThumbnailProbeResponse
    {
        public ThumbnailProbeFormat? format { get; init; }

        public ThumbnailProbeStream[]? streams { get; init; }
    }

    private sealed class ThumbnailProbeFormat
    {
        public string? duration { get; init; }
    }

    private sealed class ThumbnailProbeStream
    {
        public string? codec_type { get; init; }
    }
}
