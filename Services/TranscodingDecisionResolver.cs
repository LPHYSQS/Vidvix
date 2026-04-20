using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Vidvix.Core.Interfaces;
using Vidvix.Core.Models;
using Vidvix.Services.FFmpeg;

namespace Vidvix.Services;

public sealed class TranscodingDecisionResolver
{
    private readonly IFFmpegVideoAccelerationService _ffmpegVideoAccelerationService;
    private readonly ILocalizationService _localizationService;

    public TranscodingDecisionResolver(
        IFFmpegVideoAccelerationService ffmpegVideoAccelerationService,
        ILocalizationService localizationService)
    {
        _ffmpegVideoAccelerationService = ffmpegVideoAccelerationService
            ?? throw new ArgumentNullException(nameof(ffmpegVideoAccelerationService));
        _localizationService = localizationService ?? throw new ArgumentNullException(nameof(localizationService));
    }

    public async Task<TranscodingDecision> ResolveAsync(
        string runtimeExecutablePath,
        OutputFormatOption outputFormat,
        TranscodingMode transcodingMode,
        bool isGpuAccelerationRequested,
        bool containsVideo,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runtimeExecutablePath);
        ArgumentNullException.ThrowIfNull(outputFormat);

        if (transcodingMode != TranscodingMode.FullTranscode)
        {
            return new TranscodingDecision(
                transcodingMode,
                isGpuAccelerationRequested,
                IsGpuApplicable: false,
                SupportsHardwareVideoEncoding: false,
                VideoAccelerationKind.None,
                LogLevel.Info,
                GetLocalizedText(
                    "mainWindow.transcoding.fastRemux",
                    "当前使用快速换封装模式：优先复用可兼容的原始流，不兼容的部分会回退为兼容转码。"));
        }

        if (!isGpuAccelerationRequested)
        {
            return new TranscodingDecision(
                transcodingMode,
                isGpuAccelerationRequested,
                IsGpuApplicable: false,
                SupportsHardwareVideoEncoding: false,
                VideoAccelerationKind.None,
                LogLevel.Info,
                containsVideo
                    ? GetLocalizedText(
                        "mainWindow.transcoding.fullTranscodeVideo",
                        "当前使用真正转码模式：本次将通过 CPU 重新编码输出。")
                    : GetLocalizedText(
                        "mainWindow.transcoding.fullTranscodeAudio",
                        "当前使用真正转码模式：本次将通过 CPU 音频编码输出。"));
        }

        if (!containsVideo)
        {
            return new TranscodingDecision(
                transcodingMode,
                isGpuAccelerationRequested,
                IsGpuApplicable: false,
                SupportsHardwareVideoEncoding: false,
                VideoAccelerationKind.None,
                LogLevel.Info,
                GetLocalizedText(
                    "mainWindow.transcoding.gpuAudioFallback",
                    "已开启 GPU 加速，但当前流程仅处理音频，本次继续使用 CPU 音频编码。"));
        }

        var supportsHardwareVideoEncoding = FFmpegVideoEncodingPolicy.SupportsHardwareVideoEncoding(outputFormat);
        if (!supportsHardwareVideoEncoding)
        {
            return new TranscodingDecision(
                transcodingMode,
                isGpuAccelerationRequested,
                IsGpuApplicable: false,
                SupportsHardwareVideoEncoding: supportsHardwareVideoEncoding,
                VideoAccelerationKind.None,
                LogLevel.Info,
                FormatLocalizedText(
                    "mainWindow.transcoding.gpuOutputUnsupported",
                    $"已开启 GPU 加速，但 {outputFormat.DisplayName} 不支持硬件视频编码，本次自动回退为 CPU 编码。",
                    ("format", outputFormat.DisplayName)));
        }

        var probeResult = await _ffmpegVideoAccelerationService
            .ProbeBestEncoderAsync(runtimeExecutablePath, cancellationToken)
            .ConfigureAwait(false);

        return new TranscodingDecision(
            transcodingMode,
            isGpuAccelerationRequested,
            probeResult.IsAvailable,
            supportsHardwareVideoEncoding,
            probeResult.IsAvailable ? probeResult.Kind : VideoAccelerationKind.None,
            probeResult.IsAvailable ? LogLevel.Info : LogLevel.Warning,
            probeResult.Message);
    }

    private string GetLocalizedText(string key, string fallback) =>
        _localizationService.GetString(key, fallback);

    private string FormatLocalizedText(string key, string fallback, params (string Name, object? Value)[] arguments)
    {
        Dictionary<string, object?>? localizedArguments = null;
        if (arguments.Length > 0)
        {
            localizedArguments = new Dictionary<string, object?>(arguments.Length, StringComparer.Ordinal);
            foreach (var argument in arguments)
            {
                localizedArguments[argument.Name] = argument.Value;
            }
        }

        return _localizationService.Format(key, localizedArguments, fallback);
    }
}
