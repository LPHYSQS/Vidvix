using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Vidvix.Core.Interfaces;
using Vidvix.Core.Models;

namespace Vidvix.Services.MediaInfo;

public sealed partial class MediaInfoService : IMediaInfoService
{
    private const string UnknownValue = "\u672a\u77e5";
    private const string ParseFailedMessage = "\u65e0\u6cd5\u89e3\u6790\u5f53\u524d\u5a92\u4f53\u6587\u4ef6\u3002";
    private const string MissingVideoStreamValue = "\u672a\u68c0\u6d4b\u5230\u89c6\u9891\u6d41";
    private const string MissingAudioStreamValue = "\u672a\u68c0\u6d4b\u5230\u97f3\u9891\u6d41";
    private const string LightweightProbeEntries =
        "format=duration,bit_rate,format_name,format_long_name:format_tags=encoder:" +
        "stream=codec_type,codec_name,profile,level,width,height,avg_frame_rate,r_frame_rate,duration,bit_rate," +
        "bits_per_raw_sample,bits_per_sample,pix_fmt,color_space,color_primaries,color_transfer,channels,channel_layout," +
        "sample_rate,codec_tag_string:stream_tags=encoder,DURATION,DURATION-eng,BPS,BPS-eng,variant_bitrate,BANDWIDTH," +
        "NUMBER_OF_BYTES,NUMBER_OF_BYTES-eng:stream_disposition=attached_pic";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IFFmpegRuntimeService _ffmpegRuntimeService;
    private readonly ApplicationConfiguration _configuration;
    private readonly ILogger _logger;
    private readonly ConcurrentDictionary<string, MediaDetailsSnapshot> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Task<MediaDetailsLoadResult>> _inFlightRequests = new(StringComparer.OrdinalIgnoreCase);

    public MediaInfoService(
        IFFmpegRuntimeService ffmpegRuntimeService,
        ApplicationConfiguration configuration,
        ILogger logger)
    {
        _ffmpegRuntimeService = ffmpegRuntimeService;
        _configuration = configuration;
        _logger = logger;
    }

    public bool TryGetCachedDetails(string inputPath, out MediaDetailsSnapshot snapshot)
    {
        if (!TryCreateCacheContext(inputPath, out var cacheContext, out _))
        {
            snapshot = null!;
            return false;
        }

        return _cache.TryGetValue(cacheContext.CacheKey, out snapshot!);
    }

    public async Task<MediaDetailsLoadResult> GetMediaDetailsAsync(string inputPath, CancellationToken cancellationToken = default)
    {
        if (!TryCreateCacheContext(inputPath, out var cacheContext, out var validationError))
        {
            return MediaDetailsLoadResult.Failure(validationError ?? ParseFailedMessage);
        }

        if (_cache.TryGetValue(cacheContext.CacheKey, out var cachedSnapshot))
        {
            return MediaDetailsLoadResult.Success(cachedSnapshot);
        }

        var loadTask = _inFlightRequests.GetOrAdd(
            cacheContext.CacheKey,
            _ => LoadMediaDetailsCoreAsync(cacheContext));

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

    private async Task<MediaDetailsLoadResult> LoadMediaDetailsCoreAsync(MediaCacheContext cacheContext)
    {
        string? ffprobePath = null;
        FfprobeExecutionResult? executionResult = null;

        try
        {
            var runtimeResolution = await _ffmpegRuntimeService.EnsureAvailableAsync().ConfigureAwait(false);
            ffprobePath = ResolveFfprobePath(runtimeResolution.ExecutablePath);

            if (!File.Exists(ffprobePath))
            {
                var diagnosticDetails = CreateFfprobeDiagnosticDetails(
                    ffprobePath,
                    cacheContext.InputPath,
                    executionResult,
                    "未找到内置 ffprobe 可执行文件。");
                return MediaDetailsLoadResult.Failure(
                    "\u672a\u627e\u5230\u5185\u7f6e ffprobe\uff0c\u65e0\u6cd5\u89e3\u6790\u8be5\u89c6\u9891\u6587\u4ef6\u3002",
                    diagnosticDetails,
                    isToolMissing: true);
            }

            executionResult = await ExecuteFfprobeAsync(ffprobePath, cacheContext.InputPath).ConfigureAwait(false);
            var probeExecutionResult = executionResult.Value;
            if (probeExecutionResult.ExitCode != 0)
            {
                var failureReason = ExtractFailureReason(probeExecutionResult.StandardError);
                var diagnosticDetails = CreateFfprobeDiagnosticDetails(ffprobePath, cacheContext.InputPath, probeExecutionResult);
                _logger.Log(
                    LogLevel.Warning,
                    $"ffprobe \u89e3\u6790\u5931\u8d25\uff1a{cacheContext.InputPath}\uff0c\u9000\u51fa\u7801 {probeExecutionResult.ExitCode}\uff0c\u539f\u56e0\uff1a{failureReason}{Environment.NewLine}{diagnosticDetails}");
                return MediaDetailsLoadResult.Failure(failureReason, diagnosticDetails);
            }

            if (string.IsNullOrWhiteSpace(probeExecutionResult.StandardOutput))
            {
                var diagnosticDetails = CreateFfprobeDiagnosticDetails(
                    ffprobePath,
                    cacheContext.InputPath,
                    probeExecutionResult,
                    "ffprobe 未返回可用输出。");
                _logger.Log(LogLevel.Warning, $"ffprobe \u672a\u8fd4\u56de\u53ef\u7528\u8f93\u51fa\uff1a{cacheContext.InputPath}{Environment.NewLine}{diagnosticDetails}");
                return MediaDetailsLoadResult.Failure(ParseFailedMessage, diagnosticDetails);
            }

            var probeResult = ParseProbeResult(probeExecutionResult.StandardOutput);
            var resolvedBitrates = ResolveStreamBitrates(probeResult);
            var snapshot = BuildSnapshot(cacheContext, probeResult, resolvedBitrates);
            _cache[cacheContext.CacheKey] = snapshot;
            RemoveStaleEntries(cacheContext);
            return MediaDetailsLoadResult.Success(snapshot);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (JsonException exception)
        {
            var diagnosticDetails = CreateFfprobeDiagnosticDetails(
                ffprobePath,
                cacheContext.InputPath,
                executionResult,
                $"ffprobe \u8f93\u51fa\u89e3\u6790\u5931\u8d25\uff1a{exception.Message}");
            _logger.Log(LogLevel.Warning, $"ffprobe \u8f93\u51fa\u89e3\u6790\u5931\u8d25\uff1a{cacheContext.InputPath}{Environment.NewLine}{diagnosticDetails}", exception);
            return MediaDetailsLoadResult.Failure(ParseFailedMessage, diagnosticDetails);
        }
        catch (Exception exception)
        {
            var diagnosticDetails = CreateFfprobeDiagnosticDetails(
                ffprobePath,
                cacheContext.InputPath,
                executionResult,
                $"\u8bfb\u53d6\u5a92\u4f53\u8be6\u60c5\u65f6\u53d1\u751f\u5f02\u5e38\uff1a{exception.Message}");
            _logger.Log(LogLevel.Error, $"\u8bfb\u53d6\u5a92\u4f53\u8be6\u60c5\u65f6\u53d1\u751f\u5f02\u5e38\uff1a{cacheContext.InputPath}{Environment.NewLine}{diagnosticDetails}", exception);
            return MediaDetailsLoadResult.Failure(ParseFailedMessage, diagnosticDetails);
        }
    }

    private string ResolveFfprobePath(string ffmpegExecutablePath)
    {
        var executableDirectory = Path.GetDirectoryName(ffmpegExecutablePath)
            ?? throw new InvalidOperationException("FFmpeg \u53ef\u6267\u884c\u6587\u4ef6\u8def\u5f84\u65e0\u6548\u3002");

        return Path.Combine(executableDirectory, _configuration.FFprobeExecutableFileName);
    }

    private static async Task<FfprobeExecutionResult> ExecuteFfprobeAsync(string ffprobePath, string inputPath)
    {
        using var process = new Process
        {
            StartInfo = CreateStartInfo(ffprobePath, inputPath),
            EnableRaisingEvents = true
        };

        if (!process.Start())
        {
            return new FfprobeExecutionResult(-1, string.Empty, "ffprobe \u8fdb\u7a0b\u65e0\u6cd5\u542f\u52a8\u3002");
        }

        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync().ConfigureAwait(false);

        return new FfprobeExecutionResult(
            process.ExitCode,
            await outputTask.ConfigureAwait(false),
            await errorTask.ConfigureAwait(false));
    }

    private static ProcessStartInfo CreateStartInfo(string ffprobePath, string inputPath)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = ffprobePath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        startInfo.ArgumentList.Add("-v");
        startInfo.ArgumentList.Add("quiet");
        startInfo.ArgumentList.Add("-print_format");
        startInfo.ArgumentList.Add("json");
        startInfo.ArgumentList.Add("-show_entries");
        startInfo.ArgumentList.Add(LightweightProbeEntries);
        startInfo.ArgumentList.Add(inputPath);
        return startInfo;
    }

    private void RemoveStaleEntries(MediaCacheContext cacheContext)
    {
        var cacheKeyPrefix = cacheContext.NormalizedPath + "|";
        foreach (var existingKey in _cache.Keys)
        {
            if (!existingKey.StartsWith(cacheKeyPrefix, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(existingKey, cacheContext.CacheKey, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            _cache.TryRemove(existingKey, out _);
        }
    }

    private static bool TryCreateCacheContext(string inputPath, out MediaCacheContext cacheContext, out string? validationError)
    {
        cacheContext = default;
        validationError = null;

        if (string.IsNullOrWhiteSpace(inputPath))
        {
            validationError = ParseFailedMessage;
            return false;
        }

        try
        {
            var fullPath = Path.GetFullPath(inputPath);
            if (!File.Exists(fullPath))
            {
                validationError = "\u6587\u4ef6\u4e0d\u5b58\u5728\uff0c\u65e0\u6cd5\u89e3\u6790\u5f53\u524d\u5a92\u4f53\u6587\u4ef6\u3002";
                return false;
            }

            var fileInfo = new FileInfo(fullPath);
            var normalizedPath = fullPath.ToUpperInvariant();
            cacheContext = new MediaCacheContext(
                fullPath,
                normalizedPath,
                fileInfo.Name,
                fileInfo.LastWriteTimeUtc,
                normalizedPath + "|" + fileInfo.LastWriteTimeUtc.Ticks);
            return true;
        }
        catch (Exception exception)
        {
            validationError = string.IsNullOrWhiteSpace(exception.Message) ? ParseFailedMessage : exception.Message;
            return false;
        }
    }

}
