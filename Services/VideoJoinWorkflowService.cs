// 功能：视频拼接工作流服务（处理 FFmpeg 运行时准备、多段分辨率统一与拼接执行）
// 模块：合并模块
// 说明：仅负责视频拼接业务逻辑，不涉及 UI。
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Vidvix.Core.Interfaces;
using Vidvix.Core.Models;

namespace Vidvix.Services;

public sealed class VideoJoinWorkflowService : IVideoJoinWorkflowService
{
    private const int NormalizedAudioSampleRate = 48000;
    private const string NormalizedAudioChannelLayout = "stereo";

    private readonly ApplicationConfiguration _configuration;
    private readonly IFFmpegRuntimeService _ffmpegRuntimeService;
    private readonly IFFmpegService _ffmpegService;

    public VideoJoinWorkflowService(
        ApplicationConfiguration configuration,
        IFFmpegRuntimeService ffmpegRuntimeService,
        IFFmpegService ffmpegService)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _ffmpegRuntimeService = ffmpegRuntimeService ?? throw new ArgumentNullException(nameof(ffmpegRuntimeService));
        _ffmpegService = ffmpegService ?? throw new ArgumentNullException(nameof(ffmpegService));
    }

    public Task<FFmpegRuntimeResolution> EnsureRuntimeReadyAsync(CancellationToken cancellationToken = default) =>
        _ffmpegRuntimeService.EnsureAvailableAsync(cancellationToken);

    public async Task<VideoJoinExportResult> ExportAsync(
        VideoJoinExportRequest request,
        IProgress<FFmpegProgressUpdate>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Segments.Count == 0)
        {
            throw new InvalidOperationException("当前没有可用于拼接的视频片段。");
        }

        var runtime = await _ffmpegRuntimeService.EnsureAvailableAsync(cancellationToken).ConfigureAwait(false);
        var command = CreateCommand(runtime.ExecutablePath, request);
        var options = new FFmpegExecutionOptions
        {
            Timeout = _configuration.DefaultExecutionTimeout,
            InputDuration = request.TotalDuration > TimeSpan.Zero ? request.TotalDuration : null,
            Progress = progress
        };

        var executionResult = await _ffmpegService
            .ExecuteAsync(command, options, cancellationToken)
            .ConfigureAwait(false);

        return new VideoJoinExportResult(request, executionResult);
    }

    private FFmpegCommand CreateCommand(string runtimeExecutablePath, VideoJoinExportRequest request)
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

        ApplyOutputEncoding(arguments, request.OutputFormat, request.IncludeAudio);
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

    private static void ApplyOutputEncoding(List<string> arguments, OutputFormatOption outputFormat, bool includeAudio)
    {
        switch (outputFormat.Extension.ToLowerInvariant())
        {
            case ".mp4":
                ApplyH264VideoOutput(arguments, includeAudio, addFastStart: true);
                break;
            case ".mkv":
                ApplyH264VideoOutput(arguments, includeAudio);
                break;
            case ".mov":
                ApplyH264VideoOutput(arguments, includeAudio, addFastStart: true);
                break;
            case ".m4v":
                ApplyH264VideoOutput(arguments, includeAudio, formatOverride: "mp4", addFastStart: true);
                break;
            case ".ts":
                ApplyH264VideoOutput(arguments, includeAudio, formatOverride: "mpegts");
                break;
            case ".m2ts":
                ApplyH264VideoOutput(arguments, includeAudio, formatOverride: "mpegts", m2tsMode: true);
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
        string? formatOverride = null,
        bool addFastStart = false,
        bool m2tsMode = false)
    {
        arguments.AddRange(new[] { "-c:v", "libx264", "-crf", "23", "-preset", "medium", "-pix_fmt", "yuv420p" });

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

    private enum SegmentResolutionCategory
    {
        SmallerOrEqual,
        LargerOrEqual
    }
}
