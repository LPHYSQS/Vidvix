using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Vidvix.Core.Interfaces;
using Vidvix.Core.Models;

namespace Vidvix.Services;

public sealed class AudioJoinWorkflowService : IAudioJoinWorkflowService
{
    private const int DefaultSampleRate = 48000;
    private const string NormalizedChannelLayout = "stereo";

    private readonly ApplicationConfiguration _configuration;
    private readonly IFFmpegRuntimeService _ffmpegRuntimeService;
    private readonly IFFmpegService _ffmpegService;

    public AudioJoinWorkflowService(
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

    public async Task<AudioJoinExportResult> ExportAsync(
        AudioJoinExportRequest request,
        IProgress<FFmpegProgressUpdate>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Segments.Count == 0)
        {
            throw new InvalidOperationException("当前没有可用于拼接的音频片段。");
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

        return new AudioJoinExportResult(request, executionResult);
    }

    private FFmpegCommand CreateCommand(string runtimeExecutablePath, AudioJoinExportRequest request)
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
        arguments.Add("[aout]");
        arguments.Add("-vn");
        arguments.Add("-sn");
        arguments.Add("-dn");
        arguments.Add("-ac");
        arguments.Add("2");
        arguments.Add("-ar");
        arguments.Add(GetEffectiveSampleRate(request).ToString(CultureInfo.InvariantCulture));

        ApplyOutputEncoding(arguments, request.OutputFormat, request.PresetBitrate);
        arguments.Add(request.OutputPath);
        return new FFmpegCommand(runtimeExecutablePath, arguments);
    }

    private static string BuildFilterComplex(AudioJoinExportRequest request)
    {
        var filterParts = new List<string>(request.Segments.Count + 1);
        var sampleRate = GetEffectiveSampleRate(request);
        var sampleRateText = sampleRate.ToString(CultureInfo.InvariantCulture);

        for (var index = 0; index < request.Segments.Count; index++)
        {
            var segment = request.Segments[index];
            var durationText = segment.Duration.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture);
            filterParts.Add(
                $"[{index}:a]aresample={sampleRateText},aformat=sample_fmts=fltp:sample_rates={sampleRateText}:channel_layouts={NormalizedChannelLayout},atrim=duration={durationText},asetpts=PTS-STARTPTS[a{index}]");
        }

        var concatInputs = string.Concat(Enumerable.Range(0, request.Segments.Count).Select(index => $"[a{index}]"));
        filterParts.Add($"{concatInputs}concat=n={request.Segments.Count}:v=0:a=1[aout]");
        return string.Join(";", filterParts);
    }

    private static int GetEffectiveSampleRate(AudioJoinExportRequest request) =>
        request.PresetSampleRate > 0 ? request.PresetSampleRate : DefaultSampleRate;

    private static void ApplyOutputEncoding(List<string> arguments, OutputFormatOption outputFormat, int? presetBitrate)
    {
        switch (outputFormat.Extension.ToLowerInvariant())
        {
            case ".mp3":
                ApplyLossyAudioOutput(arguments, "libmp3lame", ResolveBitrateKbps(presetBitrate, fallbackKbps: 192, minKbps: 96, maxKbps: 320));
                break;
            case ".m4a":
                ApplyLossyAudioOutput(arguments, "aac", ResolveBitrateKbps(presetBitrate, fallbackKbps: 192, minKbps: 96, maxKbps: 320), addFastStart: true);
                break;
            case ".aac":
                ApplyLossyAudioOutput(arguments, "aac", ResolveBitrateKbps(presetBitrate, fallbackKbps: 192, minKbps: 96, maxKbps: 320), formatOverride: "adts");
                break;
            case ".wav":
                arguments.AddRange(new[] { "-c:a", "pcm_s16le" });
                break;
            case ".flac":
                arguments.AddRange(new[] { "-c:a", "flac" });
                break;
            case ".wma":
                ApplyLossyAudioOutput(arguments, "wmav2", ResolveBitrateKbps(presetBitrate, fallbackKbps: 192, minKbps: 96, maxKbps: 320));
                break;
            case ".ogg":
                ApplyLossyAudioOutput(arguments, "libvorbis", ResolveBitrateKbps(presetBitrate, fallbackKbps: 192, minKbps: 96, maxKbps: 320));
                break;
            case ".opus":
                ApplyLossyAudioOutput(arguments, "libopus", ResolveBitrateKbps(presetBitrate, fallbackKbps: 160, minKbps: 48, maxKbps: 256));
                break;
            case ".aiff":
            case ".aif":
                arguments.AddRange(new[] { "-c:a", "pcm_s16be" });
                break;
            case ".mka":
                arguments.AddRange(new[] { "-c:a", "flac", "-f", "matroska" });
                break;
            default:
                throw new InvalidOperationException("不支持的音频拼接输出格式。");
        }
    }

    private static void ApplyLossyAudioOutput(
        List<string> arguments,
        string codecName,
        int bitrateKbps,
        string? formatOverride = null,
        bool addFastStart = false)
    {
        arguments.AddRange(new[] { "-c:a", codecName, "-b:a", $"{bitrateKbps}k" });

        if (!string.IsNullOrWhiteSpace(formatOverride))
        {
            arguments.AddRange(new[] { "-f", formatOverride });
        }

        if (addFastStart)
        {
            arguments.AddRange(new[] { "-movflags", "+faststart" });
        }
    }

    private static int ResolveBitrateKbps(int? presetBitrate, int fallbackKbps, int minKbps, int maxKbps)
    {
        if (presetBitrate is not > 0)
        {
            return fallbackKbps;
        }

        var rawKbps = (int)Math.Round(presetBitrate.Value / 1_000d, MidpointRounding.AwayFromZero);
        var clampedKbps = Math.Clamp(rawKbps, minKbps, maxKbps);
        return Math.Max(32, (int)(Math.Round(clampedKbps / 16d, MidpointRounding.AwayFromZero) * 16d));
    }
}
