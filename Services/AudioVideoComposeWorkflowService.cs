using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Vidvix.Core.Interfaces;
using Vidvix.Core.Models;

namespace Vidvix.Services;

public sealed class AudioVideoComposeWorkflowService : IAudioVideoComposeWorkflowService
{
    private const int NormalizedAudioSampleRate = 48_000;
    private const string NormalizedChannelLayout = "stereo";

    private readonly ApplicationConfiguration _configuration;
    private readonly IFFmpegRuntimeService _ffmpegRuntimeService;
    private readonly IFFmpegService _ffmpegService;

    public AudioVideoComposeWorkflowService(
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

    public async Task<AudioVideoComposeExportResult> ExportAsync(
        AudioVideoComposeExportRequest request,
        IProgress<FFmpegProgressUpdate>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.OutputDuration <= TimeSpan.Zero)
        {
            throw new InvalidOperationException("当前音视频合成缺少有效的目标时长。");
        }

        var runtime = await _ffmpegRuntimeService.EnsureAvailableAsync(cancellationToken).ConfigureAwait(false);
        var command = CreateCommand(runtime.ExecutablePath, request);
        var options = new FFmpegExecutionOptions
        {
            Timeout = _configuration.DefaultExecutionTimeout,
            InputDuration = request.OutputDuration,
            Progress = progress
        };

        var executionResult = await _ffmpegService
            .ExecuteAsync(command, options, cancellationToken)
            .ConfigureAwait(false);

        return new AudioVideoComposeExportResult(request, executionResult);
    }

    private FFmpegCommand CreateCommand(string runtimeExecutablePath, AudioVideoComposeExportRequest request)
    {
        var arguments = new List<string>
        {
            "-hide_banner",
            _configuration.OverwriteOutputFiles ? "-y" : "-n"
        };

        if (request.ShouldLoopVideo)
        {
            arguments.Add("-stream_loop");
            arguments.Add("-1");
        }

        arguments.Add("-i");
        arguments.Add(request.VideoSourcePath);

        if (request.ShouldLoopImportedAudio)
        {
            arguments.Add("-stream_loop");
            arguments.Add("-1");
        }

        arguments.Add("-i");
        arguments.Add(request.AudioSourcePath);
        arguments.Add("-filter_complex");
        arguments.Add(BuildFilterComplex(request));
        arguments.Add("-map");
        arguments.Add("[vout]");
        arguments.Add("-map");
        arguments.Add("[aout]");
        arguments.Add("-sn");
        arguments.Add("-dn");
        ApplyOutputEncoding(arguments, request.OutputFormat);
        arguments.Add(request.OutputPath);
        return new FFmpegCommand(runtimeExecutablePath, arguments);
    }

    private static string BuildFilterComplex(AudioVideoComposeExportRequest request)
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

        var importedAudioFilters = new List<string>
        {
            $"aresample={NormalizedAudioSampleRate}",
            $"aformat=sample_fmts=fltp:sample_rates={NormalizedAudioSampleRate}:channel_layouts={NormalizedChannelLayout}",
            $"atrim=duration={targetDurationText}",
            "asetpts=PTS-STARTPTS"
        };

        var importedVolumeFilter = BuildVolumeFilter(request.ImportedAudioVolumeDecibels);
        if (!string.IsNullOrWhiteSpace(importedVolumeFilter))
        {
            importedAudioFilters.Add(importedVolumeFilter);
        }

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

        filterParts.Add($"[1:a]{string.Join(",", importedAudioFilters)}[amusic]");

        if (request.IncludeOriginalVideoAudio)
        {
            var originalAudioFilters = new List<string>
            {
                $"aresample={NormalizedAudioSampleRate}",
                $"aformat=sample_fmts=fltp:sample_rates={NormalizedAudioSampleRate}:channel_layouts={NormalizedChannelLayout}",
                $"atrim=duration={targetDurationText}",
                "asetpts=PTS-STARTPTS"
            };

            var originalVolumeFilter = BuildVolumeFilter(request.OriginalVideoAudioVolumeDecibels);
            if (!string.IsNullOrWhiteSpace(originalVolumeFilter))
            {
                originalAudioFilters.Add(originalVolumeFilter);
            }

            filterParts.Add($"[0:a]{string.Join(",", originalAudioFilters)}[avideo]");
            filterParts.Add("[avideo][amusic]amix=inputs=2:duration=longest:dropout_transition=0[aout]");
        }
        else
        {
            filterParts.Add("[amusic]anull[aout]");
        }

        return string.Join(";", filterParts);
    }

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

    private static void ApplyOutputEncoding(List<string> arguments, OutputFormatOption outputFormat)
    {
        switch (outputFormat.Extension.ToLowerInvariant())
        {
            case ".mp4":
                ApplyH264VideoOutput(arguments, addFastStart: true);
                break;
            case ".mkv":
                ApplyH264VideoOutput(arguments);
                break;
            case ".mov":
                ApplyH264VideoOutput(arguments, addFastStart: true);
                break;
            case ".m4v":
                ApplyH264VideoOutput(arguments, formatOverride: "mp4", addFastStart: true);
                break;
            case ".ts":
                ApplyH264VideoOutput(arguments, formatOverride: "mpegts");
                break;
            case ".m2ts":
                ApplyH264VideoOutput(arguments, formatOverride: "mpegts", m2tsMode: true);
                break;
            case ".avi":
                arguments.AddRange(new[] { "-c:v", "mpeg4", "-q:v", "2", "-pix_fmt", "yuv420p" });
                arguments.AddRange(new[] { "-c:a", "libmp3lame", "-q:a", "2" });
                break;
            case ".wmv":
                arguments.AddRange(new[] { "-c:v", "wmv2", "-b:v", "4M", "-pix_fmt", "yuv420p" });
                arguments.AddRange(new[] { "-c:a", "wmav2", "-b:a", "192k" });
                break;
            case ".flv":
                arguments.AddRange(new[] { "-c:v", "flv", "-b:v", "3M", "-pix_fmt", "yuv420p" });
                arguments.AddRange(new[] { "-c:a", "libmp3lame", "-b:a", "192k" });
                break;
            case ".webm":
                arguments.AddRange(new[] { "-c:v", "libvpx-vp9", "-crf", "32", "-b:v", "0", "-pix_fmt", "yuv420p" });
                arguments.AddRange(new[] { "-c:a", "libopus", "-b:a", "160k" });
                break;
            case ".mpeg":
            case ".mpg":
                arguments.AddRange(new[] { "-c:v", "mpeg2video", "-q:v", "2", "-pix_fmt", "yuv420p", "-f", "mpeg" });
                arguments.AddRange(new[] { "-c:a", "mp2", "-b:a", "192k" });
                break;
            default:
                throw new InvalidOperationException("不支持的音视频合成输出格式。");
        }
    }

    private static void ApplyH264VideoOutput(
        List<string> arguments,
        string? formatOverride = null,
        bool addFastStart = false,
        bool m2tsMode = false)
    {
        arguments.AddRange(new[] { "-c:v", "libx264", "-crf", "23", "-preset", "medium", "-pix_fmt", "yuv420p" });
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
