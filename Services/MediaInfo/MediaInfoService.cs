using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Vidvix.Core.Interfaces;
using Vidvix.Core.Models;
using Vidvix.Services;

namespace Vidvix.Services.MediaInfo;

public sealed partial class MediaInfoService : IMediaInfoService
{
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
    private readonly ILocalizationService _localizationService;
    private readonly ILogger _logger;
    private readonly ConcurrentDictionary<string, CachedMediaDetails> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, SharedCancelableTask<MediaDetailsLoadResult>> _inFlightRequests = new(StringComparer.OrdinalIgnoreCase);

    public MediaInfoService(
        IFFmpegRuntimeService ffmpegRuntimeService,
        ApplicationConfiguration configuration,
        ILocalizationService localizationService,
        ILogger logger)
    {
        _ffmpegRuntimeService = ffmpegRuntimeService ?? throw new ArgumentNullException(nameof(ffmpegRuntimeService));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _localizationService = localizationService ?? throw new ArgumentNullException(nameof(localizationService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public bool TryGetCachedDetails(string inputPath, out MediaDetailsSnapshot snapshot)
    {
        if (!TryCreateCacheContext(inputPath, out var cacheContext, out _))
        {
            snapshot = null!;
            return false;
        }

        if (!_cache.TryGetValue(cacheContext.CacheKey, out var cachedDetails))
        {
            snapshot = null!;
            return false;
        }

        snapshot = BuildSnapshot(
            cachedDetails.CacheContext,
            cachedDetails.ProbeResult,
            cachedDetails.ResolvedBitrates);
        return true;
    }

    public async Task<MediaDetailsLoadResult> GetMediaDetailsAsync(string inputPath, CancellationToken cancellationToken = default)
    {
        if (!TryCreateCacheContext(inputPath, out var cacheContext, out var validationError))
        {
            return MediaDetailsLoadResult.Failure(validationError ?? ParseFailedMessage);
        }

        if (_cache.TryGetValue(cacheContext.CacheKey, out var cachedDetails))
        {
            return MediaDetailsLoadResult.Success(
                BuildSnapshot(
                    cachedDetails.CacheContext,
                    cachedDetails.ProbeResult,
                    cachedDetails.ResolvedBitrates));
        }

        var inFlightLoad = _inFlightRequests.GetOrAdd(
            cacheContext.CacheKey,
            _ => CreateInFlightMediaLoad(cacheContext));
        inFlightLoad.AddWaiter();

        try
        {
            return await inFlightLoad.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            ReleaseInFlightMediaLoad(cacheContext.CacheKey, inFlightLoad);
        }
    }

    private SharedCancelableTask<MediaDetailsLoadResult> CreateInFlightMediaLoad(MediaCacheContext cacheContext)
    {
        var inFlightLoad = new SharedCancelableTask<MediaDetailsLoadResult>(
            token => LoadMediaDetailsCoreAsync(cacheContext, token));

        _ = inFlightLoad.Task.ContinueWith(
            _ => CleanupInFlightMediaLoad(cacheContext.CacheKey, inFlightLoad),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        return inFlightLoad;
    }

    private void ReleaseInFlightMediaLoad(string cacheKey, SharedCancelableTask<MediaDetailsLoadResult> inFlightLoad)
    {
        var waiterCount = inFlightLoad.ReleaseWaiter();
        if (waiterCount > 0)
        {
            return;
        }

        if (!inFlightLoad.Task.IsCompleted &&
            _inFlightRequests.TryRemove(new KeyValuePair<string, SharedCancelableTask<MediaDetailsLoadResult>>(cacheKey, inFlightLoad)))
        {
            inFlightLoad.Cancel();
            return;
        }

        if (inFlightLoad.Task.IsCompleted)
        {
            CleanupInFlightMediaLoad(cacheKey, inFlightLoad);
        }
    }

    private void CleanupInFlightMediaLoad(string cacheKey, SharedCancelableTask<MediaDetailsLoadResult> inFlightLoad)
    {
        _inFlightRequests.TryRemove(new KeyValuePair<string, SharedCancelableTask<MediaDetailsLoadResult>>(cacheKey, inFlightLoad));
        inFlightLoad.Dispose();
    }

    private async Task<MediaDetailsLoadResult> LoadMediaDetailsCoreAsync(MediaCacheContext cacheContext, CancellationToken cancellationToken)
    {
        string? ffprobePath = null;
        FfprobeExecutionResult? executionResult = null;

        try
        {
            var runtimeResolution = await _ffmpegRuntimeService.EnsureAvailableAsync(cancellationToken).ConfigureAwait(false);
            ffprobePath = ResolveFfprobePath(runtimeResolution.ExecutablePath);

            if (!File.Exists(ffprobePath))
            {
                var diagnosticDetails = CreateFfprobeDiagnosticDetails(
                    ffprobePath,
                    cacheContext.InputPath,
                    executionResult,
                    GetLocalizedText(
                        "mediaDetails.diagnostic.ffprobeMissing",
                        "未找到内置 ffprobe 可执行文件。"));
                return MediaDetailsLoadResult.Failure(
                    GetLocalizedText(
                        "mediaDetails.error.ffprobeMissing",
                        "未找到内置 ffprobe，无法解析该媒体文件。"),
                    diagnosticDetails,
                    isToolMissing: true);
            }

            executionResult = await ExecuteFfprobeAsync(ffprobePath, cacheContext.InputPath, cancellationToken).ConfigureAwait(false);
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
                    GetLocalizedText(
                        "mediaDetails.diagnostic.emptyOutput",
                        "ffprobe 未返回可用输出。"));
                _logger.Log(LogLevel.Warning, $"ffprobe \u672a\u8fd4\u56de\u53ef\u7528\u8f93\u51fa\uff1a{cacheContext.InputPath}{Environment.NewLine}{diagnosticDetails}");
                return MediaDetailsLoadResult.Failure(ParseFailedMessage, diagnosticDetails);
            }

            var probeResult = ParseProbeResult(probeExecutionResult.StandardOutput);
            var resolvedBitrates = ResolveStreamBitrates(probeResult);
            var cachedDetails = new CachedMediaDetails(cacheContext, probeResult, resolvedBitrates);
            _cache[cacheContext.CacheKey] = cachedDetails;
            RemoveStaleEntries(cacheContext);
            return MediaDetailsLoadResult.Success(
                BuildSnapshot(
                    cachedDetails.CacheContext,
                    cachedDetails.ProbeResult,
                    cachedDetails.ResolvedBitrates));
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
                FormatLocalizedText(
                    "mediaDetails.diagnostic.jsonParseFailed",
                    $"ffprobe 输出解析失败：{exception.Message}",
                    ("message", exception.Message)));
            _logger.Log(LogLevel.Warning, $"ffprobe \u8f93\u51fa\u89e3\u6790\u5931\u8d25\uff1a{cacheContext.InputPath}{Environment.NewLine}{diagnosticDetails}", exception);
            return MediaDetailsLoadResult.Failure(ParseFailedMessage, diagnosticDetails);
        }
        catch (Exception exception)
        {
            var diagnosticDetails = CreateFfprobeDiagnosticDetails(
                ffprobePath,
                cacheContext.InputPath,
                executionResult,
                FormatLocalizedText(
                    "mediaDetails.diagnostic.unexpectedException",
                    $"读取媒体详情时发生异常：{exception.Message}",
                    ("message", exception.Message)));
            _logger.Log(LogLevel.Error, $"\u8bfb\u53d6\u5a92\u4f53\u8be6\u60c5\u65f6\u53d1\u751f\u5f02\u5e38\uff1a{cacheContext.InputPath}{Environment.NewLine}{diagnosticDetails}", exception);
            return MediaDetailsLoadResult.Failure(ParseFailedMessage, diagnosticDetails);
        }
    }

    private string ResolveFfprobePath(string ffmpegExecutablePath)
    {
        var executableDirectory = Path.GetDirectoryName(ffmpegExecutablePath)
            ?? throw new InvalidOperationException(GetLocalizedText(
                "mediaDetails.error.invalidFfmpegPath",
                "FFmpeg 可执行文件路径无效。"));

        return Path.Combine(executableDirectory, _configuration.FFprobeExecutableFileName);
    }

    private async Task<FfprobeExecutionResult> ExecuteFfprobeAsync(string ffprobePath, string inputPath, CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = CreateStartInfo(ffprobePath, inputPath),
            EnableRaisingEvents = true
        };
        var processId = 0;

        if (!process.Start())
        {
            return new FfprobeExecutionResult(
                -1,
                string.Empty,
                GetLocalizedText(
                    "mediaDetails.error.ffprobeStartFailed",
                    "ffprobe 进程无法启动。"));
        }

        processId = process.Id;
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();

        using var cancellationRegistration = ExternalProcessTermination.RegisterTermination(process, cancellationToken);

        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await ExternalProcessTermination.WaitForTerminationAsync(process, processId).ConfigureAwait(false);
            await Task.WhenAll(outputTask, errorTask).ConfigureAwait(false);
            throw;
        }

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

    private bool TryCreateCacheContext(string inputPath, out MediaCacheContext cacheContext, out string? validationError)
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
                validationError = GetLocalizedText(
                    "mediaDetails.error.fileMissing",
                    "文件不存在，无法解析当前媒体文件。");
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

    private string UnknownValue => GetLocalizedText("mediaDetails.common.unknownValue", "未知");

    private string ParseFailedMessage => GetLocalizedText("mediaDetails.error.parseFailed", "无法解析当前媒体文件。");

    private string MissingVideoStreamValue => GetLocalizedText("mediaDetails.video.missingStream", "未检测到视频流");

    private string MissingAudioStreamValue => GetLocalizedText("mediaDetails.audio.missingStream", "未检测到音频流");

    private string GetLocalizedText(string key, string fallback) =>
        _localizationService.GetString(key, fallback);

    private string FormatLocalizedText(
        string key,
        string fallback,
        params (string Name, object? Value)[] arguments)
    {
        if (arguments.Length == 0)
        {
            return GetLocalizedText(key, fallback);
        }

        var localizedArguments = new Dictionary<string, object?>(arguments.Length, StringComparer.Ordinal);
        foreach (var argument in arguments)
        {
            localizedArguments[argument.Name] = argument.Value;
        }

        return _localizationService.Format(key, localizedArguments, fallback);
    }
}
