using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Vidvix.Core.Interfaces;
using Vidvix.Core.Models;

namespace Vidvix.Services.MediaInfo;

public sealed class MediaInfoService : IMediaInfoService
{
    private const string UnknownValue = "\u672a\u77e5";
    private const string ParseFailedMessage = "\u65e0\u6cd5\u89e3\u6790\u8be5\u89c6\u9891\u6587\u4ef6\u3002";
    private const string MissingVideoStreamValue = "\u672a\u68c0\u6d4b\u5230\u89c6\u9891\u6d41";
    private const string MissingAudioStreamValue = "\u672a\u68c0\u6d4b\u5230\u97f3\u9891\u6d41";
    private const string LightweightProbeEntries =
        "format=duration,bit_rate,format_name,format_long_name:format_tags=encoder:" +
        "stream=codec_type,codec_name,profile,level,width,height,avg_frame_rate,r_frame_rate,duration,bit_rate," +
        "bits_per_raw_sample,bits_per_sample,pix_fmt,color_space,color_primaries,color_transfer,channels,channel_layout," +
        "sample_rate,codec_tag_string:stream_tags=encoder,DURATION,DURATION-eng,BPS,BPS-eng,variant_bitrate,BANDWIDTH," +
        "NUMBER_OF_BYTES,NUMBER_OF_BYTES-eng";

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

    private static MediaDetailsSnapshot BuildSnapshot(MediaCacheContext cacheContext, FfprobeResponse probeResult, ResolvedStreamBitrates resolvedBitrates)
    {
        var format = probeResult.format;
        var streams = probeResult.streams ?? Array.Empty<FfprobeStream>();
        var videoStream = streams.FirstOrDefault(stream => string.Equals(stream.codec_type, "video", StringComparison.OrdinalIgnoreCase));
        var audioStream = streams.FirstOrDefault(stream => string.Equals(stream.codec_type, "audio", StringComparison.OrdinalIgnoreCase));
        var subtitleStreams = streams
            .Where(stream => string.Equals(stream.codec_type, "subtitle", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var subtitleStream = subtitleStreams.FirstOrDefault();
        var mediaDurationSeconds = FirstPositive(
            ParseDurationSeconds(format?.duration),
            ResolveStreamDurationSeconds(videoStream, format),
            ResolveStreamDurationSeconds(audioStream, format),
            ResolveStreamDurationSeconds(subtitleStream, format));

        var resolutionText = FormatResolution(videoStream?.width, videoStream?.height);
        var videoProfileLevel = BuildProfileLevel(videoStream?.profile, videoStream?.level);
        var hdrType = DetermineHdrType(videoStream?.color_transfer);
        var encoderTag = ResolveEncoderTag(format, videoStream, audioStream);
        var videoMissing = videoStream is null;
        var audioMissing = audioStream is null;
        var subtitleCount = subtitleStreams.Length;
        var videoBitrateText = videoMissing ? MissingVideoStreamValue : FormatBitrate(resolvedBitrates.VideoBitrateText);
        var audioBitrateText = audioMissing ? MissingAudioStreamValue : FormatBitrate(resolvedBitrates.AudioBitrateText);
        var overviewFields = new List<MediaDetailField>
        {
            new() { Label = "\u6587\u4ef6\u540d", Value = cacheContext.FileName },
            new() { Label = "\u65f6\u957f", Value = FormatDuration(mediaDurationSeconds) },
            new() { Label = "\u603b\u7801\u7387", Value = FormatBitrate(format?.bit_rate) },
            new() { Label = "\u5c01\u88c5\u683c\u5f0f", Value = FormatContainer(format?.format_long_name, format?.format_name) },
            new() { Label = "\u5b57\u5e55\u8f68\u9053", Value = subtitleCount > 0 ? $"{subtitleCount} \u6761" : "\u672a\u68c0\u6d4b\u5230\u5b57\u5e55\u8f68\u9053" }
        };

        if (!videoMissing)
        {
            overviewFields.Insert(2, new MediaDetailField { Label = "\u5206\u8fa8\u7387", Value = resolutionText });
        }

        var videoFields = videoMissing
            ? Array.Empty<MediaDetailField>()
            : new[]
            {
                new MediaDetailField { Label = "\u7f16\u7801", Value = FormatCodec(videoStream?.codec_name) },
                new MediaDetailField { Label = "规格 / 级别", Value = videoProfileLevel },
                new MediaDetailField { Label = "\u5206\u8fa8\u7387", Value = resolutionText },
                new MediaDetailField { Label = "\u5e27\u7387", Value = FormatFrameRate(videoStream?.avg_frame_rate, videoStream?.r_frame_rate) },
                new MediaDetailField { Label = "\u89c6\u9891\u7801\u7387", Value = videoBitrateText },
                new MediaDetailField { Label = "\u8272\u6df1", Value = FormatBitDepth(videoStream?.bits_per_raw_sample, videoStream?.pix_fmt) },
                new MediaDetailField { Label = "\u50cf\u7d20\u683c\u5f0f", Value = NormalizeValue(videoStream?.pix_fmt) },
                new MediaDetailField { Label = "\u8272\u5f69\u7a7a\u95f4", Value = NormalizeValue(videoStream?.color_space) },
                new MediaDetailField { Label = "\u8272\u57df", Value = NormalizeValue(videoStream?.color_primaries) },
                new MediaDetailField { Label = "HDR \u7c7b\u578b", Value = hdrType }
            };

        var audioFields = audioMissing
            ? Array.Empty<MediaDetailField>()
            : new[]
            {
                new MediaDetailField { Label = "\u7f16\u7801", Value = FormatCodec(audioStream?.codec_name) },
                new MediaDetailField { Label = "\u58f0\u9053", Value = FormatChannels(audioStream?.channel_layout, audioStream?.channels) },
                new MediaDetailField { Label = "\u91c7\u6837\u7387", Value = FormatSampleRate(audioStream?.sample_rate) },
                new MediaDetailField { Label = "\u97f3\u9891\u7801\u7387", Value = audioBitrateText }
            };

        var advancedFields = videoMissing
            ? Array.Empty<MediaDetailField>()
            : new[]
            {
                new MediaDetailField { Label = "色度抽样", Value = DeriveChromaSubsampling(videoStream?.pix_fmt) },
                new MediaDetailField { Label = "传输特性", Value = NormalizeValue(videoStream?.color_transfer) },
                new MediaDetailField { Label = "编码器标记", Value = encoderTag }
            };

        return new MediaDetailsSnapshot
        {
            InputPath = cacheContext.InputPath,
            FileName = cacheContext.FileName,
            LastWriteTimeUtc = cacheContext.LastWriteTimeUtc,
            MediaDuration = mediaDurationSeconds is > 0
                ? TimeSpan.FromSeconds(mediaDurationSeconds.Value)
                : null,
            HasVideoStream = !videoMissing,
            HasAudioStream = !audioMissing,
            HasSubtitleStream = subtitleCount > 0,
            SubtitleStreamCount = subtitleCount,
            PrimarySubtitleCodecName = subtitleStream?.codec_name,
            OverviewFields = overviewFields,
            VideoFields = videoFields,
            AudioFields = audioFields,
            AdvancedFields = advancedFields
        };
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
                validationError = "\u6587\u4ef6\u4e0d\u5b58\u5728\uff0c\u65e0\u6cd5\u89e3\u6790\u8be5\u89c6\u9891\u6587\u4ef6\u3002";
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

    private static string ExtractFailureReason(string standardError)
    {
        if (string.IsNullOrWhiteSpace(standardError))
        {
            return ParseFailedMessage;
        }

        var lines = standardError.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return lines.LastOrDefault() ?? ParseFailedMessage;
    }

    private static string CreateFfprobeDiagnosticDetails(
        string? ffprobePath,
        string inputPath,
        FfprobeExecutionResult? executionResult,
        string? additionalMessage = null)
    {
        var builder = new StringBuilder();

        if (!string.IsNullOrWhiteSpace(additionalMessage))
        {
            builder.AppendLine(additionalMessage);
        }

        builder.AppendLine($"输入文件：{inputPath}");

        if (!string.IsNullOrWhiteSpace(ffprobePath))
        {
            builder.AppendLine($"命令：{BuildFfprobeCommandLine(ffprobePath, inputPath)}");
        }

        if (executionResult is { } result)
        {
            builder.AppendLine($"退出码：{result.ExitCode}");
            AppendDiagnosticSection(builder, "标准错误", result.StandardError);
            AppendDiagnosticSection(builder, "标准输出", result.StandardOutput);
        }

        return builder.ToString().Trim();
    }

    private static void AppendDiagnosticSection(StringBuilder builder, string title, string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return;
        }

        builder.AppendLine(title + "：");
        builder.AppendLine(TrimDiagnosticContent(content));
    }

    private static string TrimDiagnosticContent(string content)
    {
        var lines = content
            .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .TakeLast(12)
            .ToArray();

        return lines.Length == 0 ? content.Trim() : string.Join(Environment.NewLine, lines);
    }

    private static string BuildFfprobeCommandLine(string ffprobePath, string inputPath) =>
        string.Join(
            " ",
            new[]
            {
                QuoteArgument(ffprobePath),
                "-v",
                "quiet",
                "-print_format",
                "json",
                "-show_entries",
                LightweightProbeEntries,
                QuoteArgument(inputPath)
            });

    private static string QuoteArgument(string value) =>
        "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";

    private static FfprobeResponse ParseProbeResult(string json)
    {
        return JsonSerializer.Deserialize<FfprobeResponse>(json, JsonOptions)
            ?? throw new JsonException("ffprobe \u8f93\u51fa\u4e3a\u7a7a\u3002");
    }

    private static ResolvedStreamBitrates ResolveStreamBitrates(FfprobeResponse probeResult)
    {
        var streams = probeResult.streams ?? Array.Empty<FfprobeStream>();
        var format = probeResult.format;
        var videoStream = streams.FirstOrDefault(stream => string.Equals(stream.codec_type, "video", StringComparison.OrdinalIgnoreCase));
        var audioStream = streams.FirstOrDefault(stream => string.Equals(stream.codec_type, "audio", StringComparison.OrdinalIgnoreCase));

        var videoBitrateText = ResolveStreamBitrateText(videoStream, format, audioStream is null, isAudioStream: false);
        var audioBitrateText = ResolveStreamBitrateText(audioStream, format, videoStream is null, isAudioStream: true);

        return new ResolvedStreamBitrates(videoBitrateText, audioBitrateText);
    }

    private static string? ResolveStreamBitrateText(FfprobeStream? stream, FfprobeFormat? format, bool isOnlyPrimaryStream, bool isAudioStream)
    {
        if (stream is null)
        {
            return null;
        }

        var directBitrate = NormalizeNumericText(
            FirstNonEmpty(
                stream.bit_rate,
                GetTagValueIgnoreCase(stream.tags, "BPS", "BPS-eng", "variant_bitrate", "BANDWIDTH")));
        if (!string.IsNullOrWhiteSpace(directBitrate))
        {
            return directBitrate;
        }

        if (TryResolveBitrateFromTaggedBytes(stream, format, out var derivedBitrate))
        {
            return derivedBitrate;
        }

        if (isAudioStream && TryResolvePcmAudioBitrate(stream, out var pcmBitrate))
        {
            return pcmBitrate;
        }

        if (isOnlyPrimaryStream)
        {
            return NormalizeNumericText(format?.bit_rate);
        }

        return null;
    }

    private static bool TryResolveBitrateFromTaggedBytes(FfprobeStream stream, FfprobeFormat? format, out string bitrateText)
    {
        bitrateText = string.Empty;
        var durationSeconds = ResolveStreamDurationSeconds(stream, format);
        if (!TryParsePositiveDouble(GetTagValueIgnoreCase(stream.tags, "NUMBER_OF_BYTES", "NUMBER_OF_BYTES-eng"), out var bytes) ||
            durationSeconds is not > 0)
        {
            return false;
        }

        var bitsPerSecond = (bytes * 8d) / durationSeconds.Value;
        if (bitsPerSecond <= 0)
        {
            return false;
        }

        bitrateText = bitsPerSecond.ToString("0.###", CultureInfo.InvariantCulture);
        return true;
    }

    private static bool TryResolvePcmAudioBitrate(FfprobeStream stream, out string bitrateText)
    {
        bitrateText = string.Empty;

        if (string.IsNullOrWhiteSpace(stream.codec_name) ||
            !stream.codec_name.StartsWith("pcm_", StringComparison.OrdinalIgnoreCase) ||
            !TryParsePositiveDouble(stream.sample_rate, out var sampleRate) ||
            stream.channels is not > 0 ||
            stream.bits_per_sample is not > 0)
        {
            return false;
        }

        var bitsPerSecond = sampleRate * stream.channels.Value * stream.bits_per_sample.Value;
        if (bitsPerSecond <= 0)
        {
            return false;
        }

        bitrateText = bitsPerSecond.ToString("0.###", CultureInfo.InvariantCulture);
        return true;
    }

    private async Task<string?> ProbeMappedStreamBitrateAsync(string ffmpegPath, string inputPath, string mapSelector, string mediaLabel, double? durationSeconds)
    {
        if (durationSeconds is not > 0)
        {
            return null;
        }

        using var process = new Process
        {
            StartInfo = CreateBitrateProbeStartInfo(ffmpegPath, inputPath, mapSelector),
            EnableRaisingEvents = true
        };

        if (!process.Start())
        {
            return null;
        }

        var standardOutputTask = process.StandardOutput.ReadToEndAsync();
        var standardErrorTask = process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync().ConfigureAwait(false);
        await Task.WhenAll(standardOutputTask, standardErrorTask).ConfigureAwait(false);

        var standardError = standardErrorTask.Result;
        if (!TryExtractMappedStreamSizeBytes(standardError, mediaLabel, out var sizeBytes) || sizeBytes <= 0)
        {
            return null;
        }

        var bitsPerSecond = (sizeBytes * 8d) / durationSeconds.Value;
        return bitsPerSecond > 0
            ? bitsPerSecond.ToString("0.###", CultureInfo.InvariantCulture)
            : null;
    }

    private static ProcessStartInfo CreateBitrateProbeStartInfo(string ffmpegPath, string inputPath, string mapSelector)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = ffmpegPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        startInfo.ArgumentList.Add("-hide_banner");
        startInfo.ArgumentList.Add("-nostats");
        startInfo.ArgumentList.Add("-i");
        startInfo.ArgumentList.Add(inputPath);
        startInfo.ArgumentList.Add("-map");
        startInfo.ArgumentList.Add(mapSelector);
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add("copy");
        startInfo.ArgumentList.Add("-f");
        startInfo.ArgumentList.Add("null");
        startInfo.ArgumentList.Add("-");
        return startInfo;
    }

    private static bool TryExtractMappedStreamSizeBytes(string standardError, string mediaLabel, out long sizeBytes)
    {
        sizeBytes = 0;
        if (string.IsNullOrWhiteSpace(standardError))
        {
            return false;
        }

        var lines = standardError.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        for (var index = lines.Length - 1; index >= 0; index--)
        {
            var line = lines[index];
            if (!line.Contains("video:", StringComparison.OrdinalIgnoreCase) || !line.Contains("audio:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var token = ExtractLabeledSummaryToken(line, mediaLabel);
            if (string.IsNullOrWhiteSpace(token))
            {
                continue;
            }

            return TryParseSizeBytes(token, out sizeBytes);
        }

        return false;
    }

    private static string? ExtractLabeledSummaryToken(string line, string label)
    {
        var marker = label + ":";
        var startIndex = line.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (startIndex < 0)
        {
            return null;
        }

        startIndex += marker.Length;
        var endIndex = line.IndexOf(' ', startIndex);
        if (endIndex < 0)
        {
            endIndex = line.Length;
        }

        var token = line[startIndex..endIndex].Trim();
        return string.Equals(token, "N/A", StringComparison.OrdinalIgnoreCase) ? null : token;
    }

    private static bool TryParseSizeBytes(string sizeText, out long sizeBytes)
    {
        sizeBytes = 0;
        if (string.IsNullOrWhiteSpace(sizeText))
        {
            return false;
        }

        var units = new[] { "PiB", "TiB", "GiB", "MiB", "KiB", "B" };
        foreach (var unit in units)
        {
            if (!sizeText.EndsWith(unit, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var numericPortion = sizeText[..^unit.Length];
            if (!TryParsePositiveDouble(numericPortion, out var value))
            {
                return false;
            }

            var multiplier = unit.ToUpperInvariant() switch
            {
                "PIB" => 1024d * 1024d * 1024d * 1024d * 1024d,
                "TIB" => 1024d * 1024d * 1024d * 1024d,
                "GIB" => 1024d * 1024d * 1024d,
                "MIB" => 1024d * 1024d,
                "KIB" => 1024d,
                _ => 1d
            };

            sizeBytes = (long)Math.Round(value * multiplier, MidpointRounding.AwayFromZero);
            return sizeBytes > 0;
        }

        return false;
    }

    private static string? NormalizeNumericText(string? value)
    {
        return TryParsePositiveDouble(value, out var numericValue)
            ? numericValue.ToString("0.###", CultureInfo.InvariantCulture)
            : null;
    }

    private static bool TryParsePositiveDouble(string? value, out double parsedValue)
    {
        parsedValue = 0;
        return !string.IsNullOrWhiteSpace(value) &&
               double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out parsedValue) &&
               parsedValue > 0;
    }

    private static double? ResolveStreamDurationSeconds(FfprobeStream? stream, FfprobeFormat? format)
    {
        return FirstPositive(
            ParseDurationSeconds(stream?.duration),
            ParseDurationSeconds(GetTagValueIgnoreCase(stream?.tags, "DURATION", "DURATION-eng")),
            ParseDurationSeconds(format?.duration));
    }

    private static double? ParseDurationSeconds(string? durationText)
    {
        if (TryParsePositiveDouble(durationText, out var seconds))
        {
            return seconds;
        }

        if (string.IsNullOrWhiteSpace(durationText))
        {
            return null;
        }

        var segments = durationText.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length != 3 ||
            !double.TryParse(segments[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var hours) ||
            !double.TryParse(segments[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var minutes) ||
            !double.TryParse(segments[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var remainingSeconds))
        {
            return null;
        }

        var totalSeconds = (hours * 3600d) + (minutes * 60d) + remainingSeconds;
        return totalSeconds > 0 ? totalSeconds : null;
    }

    private static double? FirstPositive(params double?[] values) =>
        values.FirstOrDefault(value => value is > 0);

    private static string? GetTagValueIgnoreCase(Dictionary<string, string>? tags, params string[] keys)
    {
        if (tags is null || keys.Length == 0)
        {
            return null;
        }

        foreach (var key in keys)
        {
            foreach (var entry in tags)
            {
                if (string.Equals(entry.Key, key, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(entry.Value))
                {
                    return entry.Value;
                }
            }
        }

        return null;
    }
    private static string FormatContainer(string? formatLongName, string? formatName)
    {
        if (!string.IsNullOrWhiteSpace(formatLongName))
        {
            return formatLongName;
        }

        if (string.IsNullOrWhiteSpace(formatName))
        {
            return UnknownValue;
        }

        var primaryName = formatName.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
        return string.IsNullOrWhiteSpace(primaryName) ? UnknownValue : primaryName.ToUpperInvariant();
    }

    private static string FormatCodec(string? codecName)
    {
        if (string.IsNullOrWhiteSpace(codecName))
        {
            return UnknownValue;
        }

        return codecName.ToLowerInvariant() switch
        {
            "h264" => "H.264 / AVC",
            "hevc" => "H.265 / HEVC",
            "av1" => "AV1",
            "vp9" => "VP9",
            "mpeg4" => "MPEG-4 Part 2",
            "prores" => "ProRes",
            "aac" => "AAC",
            "mp3" => "MP3",
            "ac3" => "AC-3",
            "eac3" => "E-AC-3",
            "flac" => "FLAC",
            "opus" => "Opus",
            "vorbis" => "Vorbis",
            "wmav2" => "WMA v2",
            "truehd" => "Dolby TrueHD",
            "dts" => "DTS",
            _ when codecName.StartsWith("pcm_", StringComparison.OrdinalIgnoreCase) => "PCM",
            _ => codecName.ToUpperInvariant()
        };
    }

    private static string BuildProfileLevel(string? profile, int? level)
    {
        var profileText = string.IsNullOrWhiteSpace(profile) ? UnknownValue : profile;
        var levelText = FormatLevel(level);

        if (profileText == UnknownValue && levelText == UnknownValue)
        {
            return UnknownValue;
        }

        if (levelText == UnknownValue)
        {
            return profileText;
        }

        if (profileText == UnknownValue)
        {
            return levelText;
        }

        return profileText + " / " + levelText;
    }

    private static string FormatDuration(string? durationText) =>
        FormatDuration(ParseDurationSeconds(durationText));

    private static string FormatDuration(double? durationSeconds)
    {
        if (durationSeconds is not >= 0)
        {
            return UnknownValue;
        }

        var duration = TimeSpan.FromSeconds(durationSeconds.Value);
        if (duration.TotalHours >= 1)
        {
            return $"{(int)duration.TotalHours:00}:{duration.Minutes:00}:{duration.Seconds:00}";
        }

        if (duration.TotalMinutes >= 1)
        {
            return $"{duration.Minutes:00}:{duration.Seconds:00}";
        }

        return $"{Math.Max(duration.TotalSeconds, 0.1):F1} \u79d2";
    }

    private static string FormatResolution(int? width, int? height)
    {
        if (width is not > 0 || height is not > 0)
        {
            return UnknownValue;
        }

        return $"{width} x {height}";
    }

    private static string FormatFrameRate(string? averageFrameRate, string? realFrameRate)
    {
        var frameRate = ParseFrameRate(averageFrameRate) ?? ParseFrameRate(realFrameRate);
        return frameRate is null ? UnknownValue : $"{frameRate.Value:0.###} 帧/秒";
    }

    private static double? ParseFrameRate(string? rawFrameRate)
    {
        if (string.IsNullOrWhiteSpace(rawFrameRate))
        {
            return null;
        }

        var segments = rawFrameRate.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length == 2 &&
            double.TryParse(segments[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var numerator) &&
            double.TryParse(segments[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var denominator) &&
            denominator > 0)
        {
            return numerator / denominator;
        }

        return double.TryParse(rawFrameRate, NumberStyles.Float, CultureInfo.InvariantCulture, out var frameRate)
            ? frameRate
            : null;
    }

    private static string FormatBitrate(string? bitrateText)
    {
        if (!double.TryParse(bitrateText, NumberStyles.Float, CultureInfo.InvariantCulture, out var bitsPerSecond) || bitsPerSecond <= 0)
        {
            return UnknownValue;
        }

        if (bitsPerSecond >= 1_000_000)
        {
            return $"{bitsPerSecond / 1_000_000d:0.##} Mbps";
        }

        if (bitsPerSecond >= 1_000)
        {
            return $"{bitsPerSecond / 1_000d:0.##} kbps";
        }

        return $"{bitsPerSecond:0} 比特/秒";
    }

    private static string FormatBitDepth(string? bitsPerRawSample, string? pixelFormat)
    {
        if (int.TryParse(bitsPerRawSample, NumberStyles.Integer, CultureInfo.InvariantCulture, out var bitDepth) && bitDepth > 0)
        {
            return bitDepth + " 位";
        }

        if (string.IsNullOrWhiteSpace(pixelFormat))
        {
            return UnknownValue;
        }

        var normalized = pixelFormat.ToLowerInvariant();
        if (normalized.Contains("p16", StringComparison.Ordinal)) return "16 位";
        if (normalized.Contains("p14", StringComparison.Ordinal)) return "14 位";
        if (normalized.Contains("p12", StringComparison.Ordinal)) return "12 位";
        if (normalized.Contains("p10", StringComparison.Ordinal)) return "10 位";
        if (normalized.Contains("p9", StringComparison.Ordinal)) return "9 位";
        return "8 位";
    }

    private static string DetermineHdrType(string? colorTransfer)
    {
        if (string.Equals(colorTransfer, "smpte2084", StringComparison.OrdinalIgnoreCase))
        {
            return "HDR10";
        }

        if (string.Equals(colorTransfer, "arib-std-b67", StringComparison.OrdinalIgnoreCase))
        {
            return "HLG";
        }

        return "SDR";
    }

    private static string FormatLevel(int? level)
    {
        if (level is not > 0)
        {
            return UnknownValue;
        }

        return level.Value >= 10 && level.Value <= 99
            ? (level.Value / 10d).ToString("0.#", CultureInfo.InvariantCulture)
            : level.Value.ToString(CultureInfo.InvariantCulture);
    }

    private static string FormatSampleRate(string? sampleRateText)
    {
        if (!double.TryParse(sampleRateText, NumberStyles.Float, CultureInfo.InvariantCulture, out var sampleRate) || sampleRate <= 0)
        {
            return UnknownValue;
        }

        return sampleRate >= 1_000
            ? $"{sampleRate / 1_000d:0.###} kHz"
            : $"{sampleRate:0} Hz";
    }

    private static string FormatChannels(string? channelLayout, int? channels)
    {
        var friendlyLayout = channelLayout?.ToLowerInvariant() switch
        {
            "mono" => "\u5355\u58f0\u9053",
            "stereo" => "\u7acb\u4f53\u58f0",
            "2.1" => "2.1 \u58f0\u9053",
            "3.0" => "3.0 \u58f0\u9053",
            "4.0" => "4.0 \u58f0\u9053",
            "5.1" => "5.1 \u58f0\u9053",
            "5.1(side)" => "5.1 \u58f0\u9053",
            "7.1" => "7.1 \u58f0\u9053",
            "7.1(wide)" => "7.1 \u58f0\u9053",
            _ => channelLayout
        };

        if (!string.IsNullOrWhiteSpace(friendlyLayout) && channels is > 0)
        {
            return $"{friendlyLayout}（{channels} 声道）";
        }

        if (!string.IsNullOrWhiteSpace(friendlyLayout))
        {
            return friendlyLayout;
        }

        return channels is > 0 ? $"{channels} \u58f0\u9053" : UnknownValue;
    }

    private static string DeriveChromaSubsampling(string? pixelFormat)
    {
        if (string.IsNullOrWhiteSpace(pixelFormat))
        {
            return UnknownValue;
        }

        var normalized = pixelFormat.ToLowerInvariant();
        if (normalized.Contains("444", StringComparison.Ordinal)) return "4:4:4";
        if (normalized.Contains("422", StringComparison.Ordinal)) return "4:2:2";
        if (normalized.Contains("420", StringComparison.Ordinal)) return "4:2:0";
        if (normalized.Contains("440", StringComparison.Ordinal)) return "4:4:0";
        if (normalized.Contains("411", StringComparison.Ordinal)) return "4:1:1";
        if (normalized.Contains("410", StringComparison.Ordinal)) return "4:1:0";
        if (normalized.Contains("mono", StringComparison.Ordinal)) return "4:0:0";
        return UnknownValue;
    }

    private static string ResolveEncoderTag(FfprobeFormat? format, FfprobeStream? videoStream, FfprobeStream? audioStream)
    {
        return FirstNonEmpty(
                   GetTagValue(format?.tags, "encoder"),
                   GetTagValue(format?.tags, "ENCODER"),
                   GetTagValue(videoStream?.tags, "encoder"),
                   GetTagValue(videoStream?.tags, "ENCODER"),
                   GetTagValue(audioStream?.tags, "encoder"),
                   GetTagValue(audioStream?.tags, "ENCODER"),
                   videoStream?.codec_tag_string,
                   audioStream?.codec_tag_string) ??
               UnknownValue;
    }

    private static string? GetTagValue(Dictionary<string, string>? tags, string key)
    {
        if (tags is null)
        {
            return null;
        }

        return tags.TryGetValue(key, out var value) ? value : null;
    }

    private static string NormalizeValue(string? value) =>
        string.IsNullOrWhiteSpace(value) ? UnknownValue : value;

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    private readonly record struct ResolvedStreamBitrates(
        string? VideoBitrateText,
        string? AudioBitrateText);

    private readonly record struct MediaCacheContext(
        string InputPath,
        string NormalizedPath,
        string FileName,
        DateTime LastWriteTimeUtc,
        string CacheKey);

    private readonly record struct FfprobeExecutionResult(
        int ExitCode,
        string StandardOutput,
        string StandardError);

    private sealed class FfprobeResponse
    {
        public FfprobeFormat? format { get; init; }

        public IReadOnlyList<FfprobeStream>? streams { get; init; }
    }

    private sealed class FfprobeFormat
    {
        public string? duration { get; init; }

        public string? bit_rate { get; init; }

        public string? format_name { get; init; }

        public string? format_long_name { get; init; }

        public Dictionary<string, string>? tags { get; init; }
    }

    private sealed class FfprobeStream
    {
        public string? codec_type { get; init; }

        public string? codec_name { get; init; }

        public string? profile { get; init; }

        public int? level { get; init; }

        public int? width { get; init; }

        public int? height { get; init; }

        public string? avg_frame_rate { get; init; }

        public string? r_frame_rate { get; init; }

        public string? duration { get; init; }

        public string? bit_rate { get; init; }

        public string? bits_per_raw_sample { get; init; }

        public int? bits_per_sample { get; init; }

        public string? pix_fmt { get; init; }

        public string? color_space { get; init; }

        public string? color_primaries { get; init; }

        public string? color_transfer { get; init; }

        public int? channels { get; init; }

        public string? channel_layout { get; init; }

        public string? sample_rate { get; init; }

        public string? codec_tag_string { get; init; }

        public Dictionary<string, string>? tags { get; init; }
    }
}
