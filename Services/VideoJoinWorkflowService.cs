// 功能：视频拼接工作流服务（处理 FFmpeg 运行时准备、多段分辨率统一与拼接执行）
// 模块：合并模块
// 说明：仅负责视频拼接业务逻辑，不涉及 UI。
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Vidvix.Core.Interfaces;
using Vidvix.Core.Models;
using Vidvix.Services.FFmpeg;

namespace Vidvix.Services;

public sealed class VideoJoinWorkflowService : IVideoJoinWorkflowService
{
    private const int NormalizedAudioSampleRate = 48_000;
    private const string NormalizedAudioChannelLayout = "stereo";

    private readonly ApplicationConfiguration _configuration;
    private readonly IFFmpegRuntimeService _ffmpegRuntimeService;
    private readonly IFFmpegService _ffmpegService;
    private readonly TranscodingDecisionResolver _transcodingDecisionResolver;

    public VideoJoinWorkflowService(
        ApplicationConfiguration configuration,
        IFFmpegRuntimeService ffmpegRuntimeService,
        IFFmpegService ffmpegService,
        TranscodingDecisionResolver transcodingDecisionResolver)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _ffmpegRuntimeService = ffmpegRuntimeService ?? throw new ArgumentNullException(nameof(ffmpegRuntimeService));
        _ffmpegService = ffmpegService ?? throw new ArgumentNullException(nameof(ffmpegService));
        _transcodingDecisionResolver = transcodingDecisionResolver ?? throw new ArgumentNullException(nameof(transcodingDecisionResolver));
    }

    public Task<FFmpegRuntimeResolution> EnsureRuntimeReadyAsync(CancellationToken cancellationToken = default) =>
        _ffmpegRuntimeService.EnsureAvailableAsync(cancellationToken);

    public async Task<VideoJoinExportResult> ExportAsync(
        VideoJoinExportRequest request,
        IProgress<FFmpegProgressUpdate>? progress = null,
        Action? onCpuFallback = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Segments.Count == 0)
        {
            throw new InvalidOperationException("当前没有可用于拼接的视频片段。");
        }

        var runtime = await _ffmpegRuntimeService.EnsureAvailableAsync(cancellationToken).ConfigureAwait(false);
        var decision = await _transcodingDecisionResolver
            .ResolveAsync(
                runtime.ExecutablePath,
                request.OutputFormat,
                request.TranscodingMode,
                request.IsGpuAccelerationRequested,
                containsVideo: true,
                cancellationToken)
            .ConfigureAwait(false);

        var options = new FFmpegExecutionOptions
        {
            Timeout = _configuration.DefaultExecutionTimeout,
            InputDuration = request.TotalDuration > TimeSpan.Zero ? request.TotalDuration : null,
            Progress = progress
        };

        var resolvedRequest = request with
        {
            VideoAccelerationKind = decision.VideoAccelerationKind
        };

        var finalRequest = resolvedRequest;
        var usedFastPath = false;
        var usedCpuFallback = false;
        var transcodingMessage = request.TranscodingMode == TranscodingMode.FastContainerConversion
            ? "当前素材参数不完全一致，或当前流程需要统一分辨率 / 帧率 / 音频参数，本次已回退为兼容转码。"
            : decision.Message;

        FFmpegExecutionResult executionResult;
        string? concatListPath = null;

        try
        {
            if (request.TranscodingMode == TranscodingMode.FastContainerConversion && CanUseFastJoin(request))
            {
                concatListPath = CreateConcatListFile(request.Segments.Select(static segment => segment.SourcePath));
                executionResult = await _ffmpegService
                    .ExecuteAsync(CreateFastJoinCommand(runtime.ExecutablePath, request, concatListPath), options, cancellationToken)
                    .ConfigureAwait(false);

                if (executionResult.WasSuccessful || executionResult.WasCancelled || executionResult.TimedOut)
                {
                    usedFastPath = executionResult.WasSuccessful;
                    finalRequest = request;
                    transcodingMessage = executionResult.WasSuccessful
                        ? "当前素材与目标输出兼容，已优先复用原始流。"
                        : transcodingMessage;
                    return new VideoJoinExportResult(finalRequest, executionResult, transcodingMessage, usedFastPath, usedCpuFallback);
                }

                transcodingMessage = "当前素材无法完整复用原始流，本次已自动回退为兼容转码。";
            }

            executionResult = await _ffmpegService
                .ExecuteAsync(CreateTranscodedCommand(runtime.ExecutablePath, resolvedRequest), options, cancellationToken)
                .ConfigureAwait(false);

            if (decision.UsesHardwareVideoEncoding &&
                !executionResult.WasSuccessful &&
                !executionResult.WasCancelled &&
                !executionResult.TimedOut)
            {
                usedCpuFallback = true;
                onCpuFallback?.Invoke();

                resolvedRequest = resolvedRequest with
                {
                    VideoAccelerationKind = VideoAccelerationKind.None
                };

                executionResult = await _ffmpegService
                    .ExecuteAsync(CreateTranscodedCommand(runtime.ExecutablePath, resolvedRequest), options, cancellationToken)
                    .ConfigureAwait(false);
                transcodingMessage = "GPU 编码失败，已自动回退为 CPU 重试一次。";
            }

            finalRequest = resolvedRequest;
            return new VideoJoinExportResult(finalRequest, executionResult, transcodingMessage, usedFastPath, usedCpuFallback);
        }
        finally
        {
            TryDeleteFile(concatListPath);
        }
    }

    private FFmpegCommand CreateFastJoinCommand(string runtimeExecutablePath, VideoJoinExportRequest request, string concatListPath)
    {
        var arguments = new List<string>
        {
            "-hide_banner",
            _configuration.OverwriteOutputFiles ? "-y" : "-n",
            "-f",
            "concat",
            "-safe",
            "0",
            "-i",
            concatListPath,
            "-map",
            "0:v:0"
        };

        if (request.IncludeAudio)
        {
            arguments.Add("-map");
            arguments.Add("0:a:0");
        }
        else
        {
            arguments.Add("-an");
        }

        arguments.Add("-sn");
        arguments.Add("-dn");
        ApplyCopyOutputEncoding(arguments, request.OutputFormat, request.IncludeAudio);
        arguments.Add(request.OutputPath);
        return new FFmpegCommand(runtimeExecutablePath, arguments);
    }

    private FFmpegCommand CreateTranscodedCommand(string runtimeExecutablePath, VideoJoinExportRequest request)
    {
        var arguments = new List<string>
        {
            "-hide_banner",
            _configuration.OverwriteOutputFiles ? "-y" : "-n"
        };

        foreach (var segment in request.Segments)
        {
            arguments.Add("-i");
            arguments.Add(segment.SourcePath);
        }

        arguments.Add("-filter_complex");
        arguments.Add(BuildFilterComplex(request));
        arguments.Add("-map");
        arguments.Add("[vout]");

        if (request.IncludeAudio)
        {
            arguments.Add("-map");
            arguments.Add("[aout]");
        }
        else
        {
            arguments.Add("-an");
        }

        arguments.Add("-sn");
        arguments.Add("-dn");

        ApplyOutputEncoding(arguments, request.OutputFormat, request.IncludeAudio, request.VideoAccelerationKind);
        arguments.Add(request.OutputPath);
        return new FFmpegCommand(runtimeExecutablePath, arguments);
    }

    private static string BuildFilterComplex(VideoJoinExportRequest request)
    {
        var filterParts = new List<string>(request.Segments.Count * 2 + 1);
        var presetFrameRate = request.PresetFrameRate > 0d ? request.PresetFrameRate : 30d;
        var presetFrameRateText = presetFrameRate.ToString("0.###", CultureInfo.InvariantCulture);

        for (var index = 0; index < request.Segments.Count; index++)
        {
            var segment = request.Segments[index];
            var videoChain =
                $"[{index}:v]{BuildVideoTransformFilter(segment, request)},fps={presetFrameRateText},format=yuv420p,setsar=1,settb=AVTB,setpts=PTS-STARTPTS[v{index}]";
            filterParts.Add(videoChain);

            if (!request.IncludeAudio)
            {
                continue;
            }

            var durationText = segment.Duration.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture);
            var audioChain = segment.HasAudio
                ? $"[{index}:a]aresample={NormalizedAudioSampleRate},aformat=sample_fmts=fltp:sample_rates={NormalizedAudioSampleRate}:channel_layouts={NormalizedAudioChannelLayout},apad=pad_dur={durationText},atrim=duration={durationText},asetpts=PTS-STARTPTS[a{index}]"
                : $"anullsrc=channel_layout={NormalizedAudioChannelLayout}:sample_rate={NormalizedAudioSampleRate},atrim=duration={durationText},asetpts=PTS-STARTPTS[a{index}]";
            filterParts.Add(audioChain);
        }

        var concatInputs = string.Concat(
            Enumerable.Range(0, request.Segments.Count)
                .Select(index => request.IncludeAudio ? $"[v{index}][a{index}]" : $"[v{index}]"));
        filterParts.Add(
            request.IncludeAudio
                ? $"{concatInputs}concat=n={request.Segments.Count}:v=1:a=1[vout][aout]"
                : $"{concatInputs}concat=n={request.Segments.Count}:v=1:a=0[vout]");

        return string.Join(";", filterParts);
    }

    private static string BuildVideoTransformFilter(VideoJoinSegment segment, VideoJoinExportRequest request)
    {
        if (segment.Width == request.PresetWidth && segment.Height == request.PresetHeight)
        {
            return $"scale={request.PresetWidth}:{request.PresetHeight}:flags=lanczos";
        }

        var category = ClassifySegmentResolution(segment, request);
        if (category == SegmentResolutionCategory.SmallerOrEqual)
        {
            return request.SmallerResolutionStrategy == MergeSmallerResolutionStrategy.PadWithBlackBars
                ? $"scale={request.PresetWidth}:{request.PresetHeight}:force_original_aspect_ratio=decrease:flags=lanczos,pad={request.PresetWidth}:{request.PresetHeight}:(ow-iw)/2:(oh-ih)/2:black"
                : $"scale={request.PresetWidth}:{request.PresetHeight}:flags=lanczos";
        }

        return request.LargerResolutionStrategy == MergeLargerResolutionStrategy.CropToFill
            ? $"scale={request.PresetWidth}:{request.PresetHeight}:force_original_aspect_ratio=increase:flags=lanczos,crop={request.PresetWidth}:{request.PresetHeight}"
            : $"scale={request.PresetWidth}:{request.PresetHeight}:flags=lanczos";
    }

    private static SegmentResolutionCategory ClassifySegmentResolution(VideoJoinSegment segment, VideoJoinExportRequest request)
    {
        if (segment.Width <= request.PresetWidth && segment.Height <= request.PresetHeight)
        {
            return SegmentResolutionCategory.SmallerOrEqual;
        }

        if (segment.Width >= request.PresetWidth && segment.Height >= request.PresetHeight)
        {
            return SegmentResolutionCategory.LargerOrEqual;
        }

        var segmentArea = (long)segment.Width * segment.Height;
        var presetArea = (long)request.PresetWidth * request.PresetHeight;
        return segmentArea <= presetArea
            ? SegmentResolutionCategory.SmallerOrEqual
            : SegmentResolutionCategory.LargerOrEqual;
    }

    private static bool CanUseFastJoin(VideoJoinExportRequest request)
    {
        if (request.Segments.Count == 0)
        {
            return false;
        }

        var firstSegment = request.Segments[0];
        if (!TranscodingCompatibilityEvaluator.CanCopyVideoCodecToContainer(firstSegment.VideoCodecName, request.OutputFormat.Extension))
        {
            return false;
        }

        if (request.Segments.Any(segment =>
                segment.Width != request.PresetWidth ||
                segment.Height != request.PresetHeight ||
                !TranscodingCompatibilityEvaluator.AreFrameRatesCompatible(segment.FrameRate, request.PresetFrameRate) ||
                !string.Equals(segment.VideoCodecName, firstSegment.VideoCodecName, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        if (!request.IncludeAudio)
        {
            return true;
        }

        if (!firstSegment.HasAudio ||
            !TranscodingCompatibilityEvaluator.CanCopyAudioCodecToContainer(firstSegment.AudioCodecName, request.OutputFormat.Extension))
        {
            return false;
        }

        return request.Segments.All(segment =>
            segment.HasAudio &&
            string.Equals(segment.AudioCodecName, firstSegment.AudioCodecName, StringComparison.OrdinalIgnoreCase) &&
            segment.AudioSampleRate == firstSegment.AudioSampleRate &&
            TranscodingCompatibilityEvaluator.AreChannelLayoutsCompatible(segment.AudioChannelLayout, firstSegment.AudioChannelLayout));
    }

    private static void ApplyCopyOutputEncoding(List<string> arguments, OutputFormatOption outputFormat, bool includeAudio)
    {
        arguments.Add("-c:v");
        arguments.Add("copy");

        if (includeAudio)
        {
            arguments.Add("-c:a");
            arguments.Add("copy");
        }

        switch (outputFormat.Extension.ToLowerInvariant())
        {
            case ".mp4":
            case ".mov":
                arguments.Add("-movflags");
                arguments.Add("+faststart");
                break;
            case ".m4v":
                arguments.Add("-f");
                arguments.Add("mp4");
                arguments.Add("-movflags");
                arguments.Add("+faststart");
                break;
            case ".ts":
                arguments.Add("-f");
                arguments.Add("mpegts");
                break;
            case ".m2ts":
                arguments.Add("-f");
                arguments.Add("mpegts");
                arguments.Add("-mpegts_m2ts_mode");
                arguments.Add("1");
                break;
        }
    }

    private static void ApplyOutputEncoding(
        List<string> arguments,
        OutputFormatOption outputFormat,
        bool includeAudio,
        VideoAccelerationKind videoAccelerationKind)
    {
        switch (outputFormat.Extension.ToLowerInvariant())
        {
            case ".mp4":
                ApplyH264VideoOutput(arguments, includeAudio, videoAccelerationKind, addFastStart: true);
                break;
            case ".mkv":
                ApplyH264VideoOutput(arguments, includeAudio, videoAccelerationKind);
                break;
            case ".mov":
                ApplyH264VideoOutput(arguments, includeAudio, videoAccelerationKind, addFastStart: true);
                break;
            case ".m4v":
                ApplyH264VideoOutput(arguments, includeAudio, videoAccelerationKind, formatOverride: "mp4", addFastStart: true);
                break;
            case ".ts":
                ApplyH264VideoOutput(arguments, includeAudio, videoAccelerationKind, formatOverride: "mpegts");
                break;
            case ".m2ts":
                ApplyH264VideoOutput(arguments, includeAudio, videoAccelerationKind, formatOverride: "mpegts", m2tsMode: true);
                break;
            case ".avi":
                arguments.AddRange(new[] { "-c:v", "mpeg4", "-q:v", "2", "-pix_fmt", "yuv420p" });
                if (includeAudio)
                {
                    arguments.AddRange(new[] { "-c:a", "libmp3lame", "-q:a", "2" });
                }

                break;
            case ".wmv":
                arguments.AddRange(new[] { "-c:v", "wmv2", "-b:v", "4M", "-pix_fmt", "yuv420p" });
                if (includeAudio)
                {
                    arguments.AddRange(new[] { "-c:a", "wmav2", "-b:a", "192k" });
                }

                break;
            case ".flv":
                arguments.AddRange(new[] { "-c:v", "flv", "-b:v", "3M", "-pix_fmt", "yuv420p" });
                if (includeAudio)
                {
                    arguments.AddRange(new[] { "-c:a", "libmp3lame", "-b:a", "192k" });
                }

                break;
            case ".webm":
                arguments.AddRange(new[] { "-c:v", "libvpx-vp9", "-crf", "32", "-b:v", "0", "-pix_fmt", "yuv420p" });
                if (includeAudio)
                {
                    arguments.AddRange(new[] { "-c:a", "libopus", "-b:a", "160k" });
                }

                break;
            case ".mpeg":
            case ".mpg":
                arguments.AddRange(new[] { "-c:v", "mpeg2video", "-q:v", "2", "-pix_fmt", "yuv420p", "-f", "mpeg" });
                if (includeAudio)
                {
                    arguments.AddRange(new[] { "-c:a", "mp2", "-b:a", "192k" });
                }

                break;
            default:
                throw new InvalidOperationException("不支持的视频拼接输出格式。");
        }
    }

    private static void ApplyH264VideoOutput(
        List<string> arguments,
        bool includeAudio,
        VideoAccelerationKind videoAccelerationKind,
        string? formatOverride = null,
        bool addFastStart = false,
        bool m2tsMode = false)
    {
        FFmpegVideoEncodingPolicy.AppendH264Encoding(arguments, videoAccelerationKind);

        if (includeAudio)
        {
            arguments.AddRange(new[] { "-c:a", "aac", "-b:a", "256k" });
        }

        if (!string.IsNullOrWhiteSpace(formatOverride))
        {
            arguments.AddRange(new[] { "-f", formatOverride });
        }

        if (m2tsMode)
        {
            arguments.AddRange(new[] { "-mpegts_m2ts_mode", "1" });
        }

        if (addFastStart)
        {
            arguments.AddRange(new[] { "-movflags", "+faststart" });
        }
    }

    private static string CreateConcatListFile(IEnumerable<string> inputPaths)
    {
        var concatListPath = Path.Combine(Path.GetTempPath(), $"vidvix-video-join-{Guid.NewGuid():N}.txt");
        var lines = inputPaths.Select(static path => $"file '{EscapeConcatPath(path)}'");
        File.WriteAllLines(concatListPath, lines);
        return concatListPath;
    }

    private static string EscapeConcatPath(string path) =>
        path.Replace("\\", "/", StringComparison.Ordinal)
            .Replace("'", "'\\''", StringComparison.Ordinal);

    private static void TryDeleteFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // 忽略临时 concat list 清理失败，避免吞掉主流程结果。
        }
    }

    private enum SegmentResolutionCategory
    {
        SmallerOrEqual,
        LargerOrEqual
    }
}
