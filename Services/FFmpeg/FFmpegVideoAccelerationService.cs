using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Vidvix.Core.Interfaces;
using Vidvix.Core.Models;

namespace Vidvix.Services.FFmpeg;

public sealed class FFmpegVideoAccelerationService : IFFmpegVideoAccelerationService
{
    private static readonly IReadOnlyList<ProbeCandidate> Candidates =
        new[]
        {
            new ProbeCandidate(VideoAccelerationKind.NvidiaNvenc, "NVIDIA NVENC", "h264_nvenc"),
            new ProbeCandidate(VideoAccelerationKind.IntelQuickSync, "Intel Quick Sync", "h264_qsv"),
            new ProbeCandidate(VideoAccelerationKind.AmdAmf, "AMD AMF", "h264_amf")
        };

    private readonly IFFmpegService _ffmpegService;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _probeLock = new(1, 1);
    private string? _cachedExecutablePath;
    private VideoAccelerationProbeResult? _cachedResult;

    public FFmpegVideoAccelerationService(IFFmpegService ffmpegService, ILogger logger)
    {
        _ffmpegService = ffmpegService;
        _logger = logger;
    }

    public async Task<VideoAccelerationProbeResult> ProbeBestEncoderAsync(
        string ffmpegExecutablePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ffmpegExecutablePath);

        if (TryGetCachedResult(ffmpegExecutablePath, out var cachedResult))
        {
            return cachedResult;
        }

        await _probeLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (TryGetCachedResult(ffmpegExecutablePath, out cachedResult))
            {
                return cachedResult;
            }

            foreach (var candidate in Candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var result = await _ffmpegService.ExecuteAsync(
                    CreateProbeCommand(ffmpegExecutablePath, candidate),
                    new FFmpegExecutionOptions
                    {
                        Timeout = TimeSpan.FromSeconds(12)
                    },
                    cancellationToken).ConfigureAwait(false);

                if (result.WasSuccessful)
                {
                    var successResult = new VideoAccelerationProbeResult
                    {
                        Kind = candidate.Kind,
                        DisplayName = candidate.DisplayName,
                        EncoderName = candidate.EncoderName,
                        Message = $"检测到 {candidate.DisplayName} 可用，本次可启用视频硬件编码。"
                    };

                    _logger.Log(LogLevel.Info, $"视频硬件加速检测成功：{candidate.DisplayName}。");
                    CacheResult(ffmpegExecutablePath, successResult);
                    return successResult;
                }
            }

            var unavailableResult = new VideoAccelerationProbeResult
            {
                Kind = VideoAccelerationKind.None,
                DisplayName = "CPU",
                EncoderName = string.Empty,
                Message = "未检测到可用的 NVIDIA、Intel 或 AMD 视频硬件编码能力，将自动回退为 CPU 转码。"
            };

            _logger.Log(LogLevel.Warning, "未检测到可用的视频硬件加速编码器，将继续使用 CPU 转码。");
            CacheResult(ffmpegExecutablePath, unavailableResult);
            return unavailableResult;
        }
        finally
        {
            _probeLock.Release();
        }
    }

    private bool TryGetCachedResult(string ffmpegExecutablePath, out VideoAccelerationProbeResult result)
    {
        if (_cachedResult is not null &&
            string.Equals(_cachedExecutablePath, ffmpegExecutablePath, StringComparison.OrdinalIgnoreCase))
        {
            result = _cachedResult;
            return true;
        }

        result = null!;
        return false;
    }

    private void CacheResult(string ffmpegExecutablePath, VideoAccelerationProbeResult result)
    {
        _cachedExecutablePath = ffmpegExecutablePath;
        _cachedResult = result;
    }

    private static FFmpegCommand CreateProbeCommand(string ffmpegExecutablePath, ProbeCandidate candidate)
    {
        var arguments = new[]
        {
            "-hide_banner",
            "-loglevel",
            "error",
            "-f",
            "lavfi",
            "-i",
            "color=c=black:s=64x64:r=1",
            "-vf",
            "format=nv12",
            "-frames:v",
            "1",
            "-an",
            "-c:v",
            candidate.EncoderName,
            "-f",
            "null",
            "-"
        };

        return new FFmpegCommand(ffmpegExecutablePath, arguments);
    }

    private readonly record struct ProbeCandidate(
        VideoAccelerationKind Kind,
        string DisplayName,
        string EncoderName);
}
