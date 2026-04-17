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

public sealed class AudioJoinWorkflowService : IAudioJoinWorkflowService
{
    private const int DefaultSampleRate = 48_000;
    private const string NormalizedChannelLayout = "stereo";

    private readonly ApplicationConfiguration _configuration;
    private readonly IFFmpegRuntimeService _ffmpegRuntimeService;
    private readonly IFFmpegService _ffmpegService;
    private readonly TranscodingDecisionResolver _transcodingDecisionResolver;

    public AudioJoinWorkflowService(
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
        var decision = await _transcodingDecisionResolver
            .ResolveAsync(
                runtime.ExecutablePath,
                request.OutputFormat,
                request.TranscodingMode,
                request.IsGpuAccelerationRequested,
                containsVideo: false,
                cancellationToken)
            .ConfigureAwait(false);

        var options = new FFmpegExecutionOptions
        {
            Timeout = _configuration.DefaultExecutionTimeout,
            InputDuration = request.TotalDuration > TimeSpan.Zero ? request.TotalDuration : null,
            Progress = progress
        };

        var usedFastPath = false;
        var transcodingMessage = request.TranscodingMode == TranscodingMode.FastContainerConversion
            ? "当前素材参数不完全一致，或当前流程需要统一采样率 / 声道布局，本次已回退为兼容音频转码。"
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
                    transcodingMessage = executionResult.WasSuccessful
                        ? "当前素材与目标输出兼容，已优先复用原始音频流。"
                        : transcodingMessage;
                    return new AudioJoinExportResult(request, executionResult, transcodingMessage, usedFastPath);
                }

                transcodingMessage = "当前素材无法完整复用原始音频流，本次已自动回退为兼容音频转码。";
            }

            executionResult = await _ffmpegService
                .ExecuteAsync(CreateTranscodedCommand(runtime.ExecutablePath, request), options, cancellationToken)
                .ConfigureAwait(false);

            return new AudioJoinExportResult(request, executionResult, transcodingMessage, usedFastPath);
        }
        finally
        {
            TryDeleteFile(concatListPath);
        }
    }

    private FFmpegCommand CreateFastJoinCommand(string runtimeExecutablePath, AudioJoinExportRequest request, string concatListPath)
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
            "0:a:0",
            "-vn",
            "-sn",
            "-dn",
            "-c:a",
            "copy"
        };

        ApplyCopyOutputEncoding(arguments, request.OutputFormat);
        arguments.Add(request.OutputPath);
        return new FFmpegCommand(runtimeExecutablePath, arguments);
    }

    private FFmpegCommand CreateTranscodedCommand(string runtimeExecutablePath, AudioJoinExportRequest request)
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

        ApplyOutputEncoding(arguments, request.OutputFormat, request.ParameterMode, request.TargetBitrate);
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
        request.TargetSampleRate > 0 ? request.TargetSampleRate : DefaultSampleRate;

    private static bool CanUseFastJoin(AudioJoinExportRequest request)
    {
        if (request.Segments.Count == 0)
        {
            return false;
        }

        var firstSegment = request.Segments[0];
        if (!TranscodingCompatibilityEvaluator.CanCopyAudioCodecToContainer(firstSegment.AudioCodecName, request.OutputFormat.Extension))
        {
            return false;
        }

        var effectiveSampleRate = GetEffectiveSampleRate(request);
        if (effectiveSampleRate != firstSegment.SampleRate)
        {
            return false;
        }

        return request.Segments.All(segment =>
            string.Equals(segment.AudioCodecName, firstSegment.AudioCodecName, StringComparison.OrdinalIgnoreCase) &&
            segment.SampleRate == firstSegment.SampleRate &&
            TranscodingCompatibilityEvaluator.AreChannelLayoutsCompatible(segment.AudioChannelLayout, firstSegment.AudioChannelLayout));
    }

    private static void ApplyCopyOutputEncoding(List<string> arguments, OutputFormatOption outputFormat)
    {
        switch (outputFormat.Extension.ToLowerInvariant())
        {
            case ".m4a":
                arguments.Add("-movflags");
                arguments.Add("+faststart");
                break;
            case ".aac":
                arguments.Add("-f");
                arguments.Add("adts");
                break;
            case ".mka":
                arguments.Add("-f");
                arguments.Add("matroska");
                break;
        }
    }

    private static void ApplyOutputEncoding(
        List<string> arguments,
        OutputFormatOption outputFormat,
        AudioJoinParameterMode parameterMode,
        int? targetBitrate)
    {
        switch (outputFormat.Extension.ToLowerInvariant())
        {
            case ".mp3":
                ApplyLossyAudioOutput(
                    arguments,
                    "libmp3lame",
                    ResolveBitrateKbps(targetBitrate, parameterMode, fallbackKbps: 192, minKbps: 96, maxKbps: 320));
                break;
            case ".m4a":
                ApplyLossyAudioOutput(
                    arguments,
                    "aac",
                    ResolveBitrateKbps(targetBitrate, parameterMode, fallbackKbps: 192, minKbps: 96, maxKbps: 320),
                    addFastStart: true);
                break;
            case ".aac":
                ApplyLossyAudioOutput(
                    arguments,
                    "aac",
                    ResolveBitrateKbps(targetBitrate, parameterMode, fallbackKbps: 192, minKbps: 96, maxKbps: 320),
                    formatOverride: "adts");
                break;
            case ".wav":
                arguments.AddRange(new[] { "-c:a", "pcm_s16le" });
                break;
            case ".flac":
                arguments.AddRange(new[] { "-c:a", "flac" });
                break;
            case ".wma":
                ApplyLossyAudioOutput(
                    arguments,
                    "wmav2",
                    ResolveBitrateKbps(targetBitrate, parameterMode, fallbackKbps: 192, minKbps: 96, maxKbps: 320));
                break;
            case ".ogg":
                ApplyLossyAudioOutput(
                    arguments,
                    "libvorbis",
                    ResolveBitrateKbps(targetBitrate, parameterMode, fallbackKbps: 192, minKbps: 96, maxKbps: 320));
                break;
            case ".opus":
                ApplyLossyAudioOutput(
                    arguments,
                    "libopus",
                    ResolveBitrateKbps(targetBitrate, parameterMode, fallbackKbps: 160, minKbps: 48, maxKbps: 256),
                    disableVariableBitrate: parameterMode == AudioJoinParameterMode.Preset);
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
        bool addFastStart = false,
        bool disableVariableBitrate = false)
    {
        arguments.AddRange(new[] { "-c:a", codecName, "-b:a", $"{bitrateKbps}k" });

        if (disableVariableBitrate)
        {
            arguments.AddRange(new[] { "-vbr", "off" });
        }

        if (!string.IsNullOrWhiteSpace(formatOverride))
        {
            arguments.AddRange(new[] { "-f", formatOverride });
        }

        if (addFastStart)
        {
            arguments.AddRange(new[] { "-movflags", "+faststart" });
        }
    }

    private static int ResolveBitrateKbps(
        int? targetBitrate,
        AudioJoinParameterMode parameterMode,
        int fallbackKbps,
        int minKbps,
        int maxKbps)
    {
        if (targetBitrate is not > 0)
        {
            return fallbackKbps;
        }

        var rawKbps = (int)Math.Round(targetBitrate.Value / 1_000d, MidpointRounding.AwayFromZero);
        var clampedKbps = Math.Clamp(rawKbps, minKbps, maxKbps);
        return parameterMode == AudioJoinParameterMode.Preset
            ? clampedKbps
            : Math.Max(32, (int)(Math.Round(clampedKbps / 16d, MidpointRounding.AwayFromZero) * 16d));
    }

    private static string CreateConcatListFile(IEnumerable<string> inputPaths)
    {
        var concatListPath = Path.Combine(Path.GetTempPath(), $"vidvix-audio-join-{Guid.NewGuid():N}.txt");
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
}
