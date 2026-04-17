using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Vidvix.Core.Interfaces;
using Vidvix.Core.Models;
using Vidvix.Services.FFmpeg;

namespace Vidvix.Services;

public sealed class AudioVideoComposeWorkflowService : IAudioVideoComposeWorkflowService
{
    private const int NormalizedAudioSampleRate = 48_000;
    private const string NormalizedChannelLayout = "stereo";

    private readonly ApplicationConfiguration _configuration;
    private readonly IFFmpegRuntimeService _ffmpegRuntimeService;
    private readonly IFFmpegService _ffmpegService;
    private readonly TranscodingDecisionResolver _transcodingDecisionResolver;

    public AudioVideoComposeWorkflowService(
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

    public async Task<AudioVideoComposeExportResult> ExportAsync(
        AudioVideoComposeExportRequest request,
        IProgress<FFmpegProgressUpdate>? progress = null,
        Action? onCpuFallback = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.OutputDuration <= TimeSpan.Zero)
        {
            throw new InvalidOperationException("当前音视频合成缺少有效的目标时长。");
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
            InputDuration = request.OutputDuration,
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
            ? "当前流程无法完整复用原始视频流，本次将回退为兼容转码。"
            : decision.Message;

        FFmpegExecutionResult executionResult;

        if (request.TranscodingMode == TranscodingMode.FastContainerConversion && CanUseFastCompose(request))
        {
            executionResult = await _ffmpegService
                .ExecuteAsync(CreateFastComposeCommand(runtime.ExecutablePath, request), options, cancellationToken)
                .ConfigureAwait(false);

            if (executionResult.WasSuccessful || executionResult.WasCancelled || executionResult.TimedOut)
            {
                usedFastPath = executionResult.WasSuccessful;
                finalRequest = request;
                transcodingMessage = executionResult.WasSuccessful
                    ? "当前素材与目标输出兼容，已优先复用原始视频流。"
                    : transcodingMessage;
                return new AudioVideoComposeExportResult(finalRequest, executionResult, transcodingMessage, usedFastPath, usedCpuFallback);
            }

            transcodingMessage = "当前流程无法完整复用原始视频流，本次已自动回退为兼容转码。";
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
        return new AudioVideoComposeExportResult(finalRequest, executionResult, transcodingMessage, usedFastPath, usedCpuFallback);
    }

    private FFmpegCommand CreateFastComposeCommand(string runtimeExecutablePath, AudioVideoComposeExportRequest request)
    {
        var arguments = CreateBaseArguments();
        AddVideoInput(arguments, request.ShouldLoopVideo, request.VideoSourcePath);
        AddAudioInput(arguments, request.ShouldLoopImportedAudio, request.AudioSourcePath);

        arguments.Add("-filter_complex");
        arguments.Add(BuildAudioOnlyFilterComplex(request));
        arguments.Add("-map");
        arguments.Add("0:v:0");
        arguments.Add("-map");
        arguments.Add("[aout]");
        arguments.Add("-sn");
        arguments.Add("-dn");
        arguments.Add("-t");
        arguments.Add(FormatSeconds(request.OutputDuration.TotalSeconds));
        ApplyOutputEncoding(arguments, request.OutputFormat, VideoAccelerationKind.None, copyVideo: true);
        arguments.Add(request.OutputPath);
        return new FFmpegCommand(runtimeExecutablePath, arguments);
    }

    private FFmpegCommand CreateTranscodedCommand(string runtimeExecutablePath, AudioVideoComposeExportRequest request)
    {
        var arguments = CreateBaseArguments();
        AddVideoInput(arguments, request.ShouldLoopVideo, request.VideoSourcePath);
        AddAudioInput(arguments, request.ShouldLoopImportedAudio, request.AudioSourcePath);

        arguments.Add("-filter_complex");
        arguments.Add(BuildTranscodedFilterComplex(request));
        arguments.Add("-map");
        arguments.Add("[vout]");
        arguments.Add("-map");
        arguments.Add("[aout]");
        arguments.Add("-sn");
        arguments.Add("-dn");
        ApplyOutputEncoding(arguments, request.OutputFormat, request.VideoAccelerationKind);
        arguments.Add(request.OutputPath);
        return new FFmpegCommand(runtimeExecutablePath, arguments);
    }

    private List<string> CreateBaseArguments() =>
        new()
        {
            "-hide_banner",
            _configuration.OverwriteOutputFiles ? "-y" : "-n"
        };

    private static void AddVideoInput(ICollection<string> arguments, bool shouldLoopVideo, string videoSourcePath)
    {
        if (shouldLoopVideo)
        {
            arguments.Add("-stream_loop");
            arguments.Add("-1");
        }

        arguments.Add("-i");
        arguments.Add(videoSourcePath);
    }

    private static void AddAudioInput(ICollection<string> arguments, bool shouldLoopImportedAudio, string audioSourcePath)
    {
        if (shouldLoopImportedAudio)
        {
            arguments.Add("-stream_loop");
            arguments.Add("-1");
        }

        arguments.Add("-i");
        arguments.Add(audioSourcePath);
    }

    private static bool CanUseFastCompose(AudioVideoComposeExportRequest request) =>
        !request.ShouldLoopVideo &&
        !request.ShouldFreezeLastFrame &&
        request.VideoDuration > TimeSpan.Zero &&
        TranscodingCompatibilityEvaluator.CanCopyVideoCodecToContainer(request.VideoCodecName, request.OutputFormat.Extension);

    private static string BuildAudioOnlyFilterComplex(AudioVideoComposeExportRequest request)
    {
        var targetDurationText = FormatSeconds(request.OutputDuration.TotalSeconds);
        return string.Join(";", BuildAudioFilterParts(request, targetDurationText));
    }

    private static string BuildTranscodedFilterComplex(AudioVideoComposeExportRequest request)
    {
        var targetDurationSeconds = request.OutputDuration.TotalSeconds;
        var targetDurationText = FormatSeconds(targetDurationSeconds);
        var videoFrameRateText = FormatSeconds(request.VideoFrameRate > 0d ? request.VideoFrameRate : 30d);
        var filterParts = new List<string>(4);

        var videoFilters = new List<string>
        {
            "settb=AVTB",
            $"fps={videoFrameRateText}",
            "format=yuv420p",
            "setsar=1"
        };

        if (request.ShouldFreezeLastFrame)
        {
            var freezeDuration = Math.Max(0d, (request.AudioDuration - request.VideoDuration).TotalSeconds);
            if (freezeDuration > 0d)
            {
                videoFilters.Add($"tpad=stop_mode=clone:stop_duration={FormatSeconds(freezeDuration)}");
            }
        }

        videoFilters.Add($"trim=duration={targetDurationText}");
        videoFilters.Add("setpts=PTS-STARTPTS");
        filterParts.Add($"[0:v]{string.Join(",", videoFilters)}[vout]");
        filterParts.AddRange(BuildAudioFilterParts(request, targetDurationText));

        return string.Join(";", filterParts);
    }

    private static IEnumerable<string> BuildAudioFilterParts(AudioVideoComposeExportRequest request, string targetDurationText)
    {
        var filterParts = new List<string>(3)
        {
            $"[1:a]{string.Join(",", BuildImportedAudioFilters(request, targetDurationText))}[amusic]"
        };

        if (request.IncludeOriginalVideoAudio)
        {
            filterParts.Add($"[0:a]{string.Join(",", BuildOriginalVideoAudioFilters(request, targetDurationText))}[avideo]");
            filterParts.Add("[avideo][amusic]amix=inputs=2:duration=longest:dropout_transition=0[aout]");
        }
        else
        {
            filterParts.Add("[amusic]anull[aout]");
        }

        return filterParts;
    }

    private static List<string> BuildImportedAudioFilters(AudioVideoComposeExportRequest request, string targetDurationText)
    {
        var importedAudioFilters = CreateBaseAudioFilters(targetDurationText);

        var importedVolumeFilter = BuildVolumeFilter(request.ImportedAudioVolumeDecibels);
        if (!string.IsNullOrWhiteSpace(importedVolumeFilter))
        {
            importedAudioFilters.Add(importedVolumeFilter);
        }

        var targetDurationSeconds = request.OutputDuration.TotalSeconds;
        var fadeInDuration = request.EnableImportedAudioFadeIn
            ? Math.Clamp(request.ImportedAudioFadeInDuration.TotalSeconds, 0d, targetDurationSeconds)
            : 0d;
        if (fadeInDuration > 0d)
        {
            importedAudioFilters.Add($"afade=t=in:st=0:d={FormatSeconds(fadeInDuration)}");
        }

        var fadeOutDuration = request.EnableImportedAudioFadeOut
            ? Math.Clamp(request.ImportedAudioFadeOutDuration.TotalSeconds, 0d, targetDurationSeconds)
            : 0d;
        if (fadeOutDuration > 0d)
        {
            var fadeOutStart = Math.Max(0d, targetDurationSeconds - fadeOutDuration);
            importedAudioFilters.Add($"afade=t=out:st={FormatSeconds(fadeOutStart)}:d={FormatSeconds(fadeOutDuration)}");
        }

        return importedAudioFilters;
    }

    private static List<string> BuildOriginalVideoAudioFilters(AudioVideoComposeExportRequest request, string targetDurationText)
    {
        var originalAudioFilters = CreateBaseAudioFilters(targetDurationText);
        var originalVolumeFilter = BuildVolumeFilter(request.OriginalVideoAudioVolumeDecibels);
        if (!string.IsNullOrWhiteSpace(originalVolumeFilter))
        {
            originalAudioFilters.Add(originalVolumeFilter);
        }

        return originalAudioFilters;
    }

    private static List<string> CreateBaseAudioFilters(string targetDurationText) =>
        new()
        {
            $"aresample={NormalizedAudioSampleRate}",
            $"aformat=sample_fmts=fltp:sample_rates={NormalizedAudioSampleRate}:channel_layouts={NormalizedChannelLayout}",
            $"atrim=duration={targetDurationText}",
            "asetpts=PTS-STARTPTS"
        };

    private static string? BuildVolumeFilter(double decibels)
    {
        if (Math.Abs(decibels) < 0.001d)
        {
            return null;
        }

        var multiplier = Math.Pow(10d, decibels / 20d);
        return $"volume={multiplier.ToString("0.########", CultureInfo.InvariantCulture)}";
    }

    private static string FormatSeconds(double seconds) =>
        seconds.ToString("0.###", CultureInfo.InvariantCulture);

    private static void ApplyOutputEncoding(
        List<string> arguments,
        OutputFormatOption outputFormat,
        VideoAccelerationKind videoAccelerationKind,
        bool copyVideo = false)
    {
        switch (outputFormat.Extension.ToLowerInvariant())
        {
            case ".mp4":
                ApplyH264VideoOutput(arguments, videoAccelerationKind, copyVideo, addFastStart: true);
                break;
            case ".mkv":
                ApplyH264VideoOutput(arguments, videoAccelerationKind, copyVideo);
                break;
            case ".mov":
                ApplyH264VideoOutput(arguments, videoAccelerationKind, copyVideo, addFastStart: true);
                break;
            case ".m4v":
                ApplyH264VideoOutput(arguments, videoAccelerationKind, copyVideo, formatOverride: "mp4", addFastStart: true);
                break;
            case ".ts":
                ApplyH264VideoOutput(arguments, videoAccelerationKind, copyVideo, formatOverride: "mpegts");
                break;
            case ".m2ts":
                ApplyH264VideoOutput(arguments, videoAccelerationKind, copyVideo, formatOverride: "mpegts", m2tsMode: true);
                break;
            case ".avi":
                arguments.AddRange(copyVideo
                    ? new[] { "-c:v", "copy" }
                    : new[] { "-c:v", "mpeg4", "-q:v", "2", "-pix_fmt", "yuv420p" });
                arguments.AddRange(new[] { "-c:a", "libmp3lame", "-q:a", "2" });
                break;
            case ".wmv":
                arguments.AddRange(copyVideo
                    ? new[] { "-c:v", "copy" }
                    : new[] { "-c:v", "wmv2", "-b:v", "4M", "-pix_fmt", "yuv420p" });
                arguments.AddRange(new[] { "-c:a", "wmav2", "-b:a", "192k" });
                break;
            case ".flv":
                arguments.AddRange(copyVideo
                    ? new[] { "-c:v", "copy" }
                    : new[] { "-c:v", "flv", "-b:v", "3M", "-pix_fmt", "yuv420p" });
                arguments.AddRange(new[] { "-c:a", "libmp3lame", "-b:a", "192k" });
                break;
            case ".webm":
                arguments.AddRange(copyVideo
                    ? new[] { "-c:v", "copy" }
                    : new[] { "-c:v", "libvpx-vp9", "-crf", "32", "-b:v", "0", "-pix_fmt", "yuv420p" });
                arguments.AddRange(new[] { "-c:a", "libopus", "-b:a", "160k" });
                break;
            case ".mpeg":
            case ".mpg":
                arguments.AddRange(copyVideo
                    ? new[] { "-c:v", "copy", "-f", "mpeg" }
                    : new[] { "-c:v", "mpeg2video", "-q:v", "2", "-pix_fmt", "yuv420p", "-f", "mpeg" });
                arguments.AddRange(new[] { "-c:a", "mp2", "-b:a", "192k" });
                break;
            default:
                throw new InvalidOperationException("不支持的音视频合成输出格式。");
        }
    }

    private static void ApplyH264VideoOutput(
        List<string> arguments,
        VideoAccelerationKind videoAccelerationKind,
        bool copyVideo,
        string? formatOverride = null,
        bool addFastStart = false,
        bool m2tsMode = false)
    {
        if (copyVideo)
        {
            arguments.AddRange(new[] { "-c:v", "copy" });
        }
        else
        {
            FFmpegVideoEncodingPolicy.AppendH264Encoding(arguments, videoAccelerationKind);
        }

        arguments.AddRange(new[] { "-c:a", "aac", "-b:a", "256k" });

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
}
