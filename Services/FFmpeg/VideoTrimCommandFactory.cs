using System;
using System.IO;
using Vidvix.Core.Interfaces;
using Vidvix.Core.Models;

namespace Vidvix.Services.FFmpeg;

public sealed class VideoTrimCommandFactory : IVideoTrimCommandFactory
{
    private readonly ApplicationConfiguration _configuration;
    private readonly IFFmpegCommandBuilder _commandBuilder;

    public VideoTrimCommandFactory(
        ApplicationConfiguration configuration,
        IFFmpegCommandBuilder commandBuilder)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _commandBuilder = commandBuilder ?? throw new ArgumentNullException(nameof(commandBuilder));
    }

    public FFmpegCommand Create(VideoTrimExportRequest request, string runtimeExecutablePath)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(runtimeExecutablePath);

        if (request.EndPosition <= request.StartPosition)
        {
            throw new InvalidOperationException("裁剪结束时间必须大于开始时间。");
        }

        if (request.TranscodingMode == TranscodingMode.FastContainerConversion &&
            SupportsFastContainerCopy(request.InputPath, request.OutputFormat.Extension))
        {
            return BuildStreamCopyCommand(request, runtimeExecutablePath);
        }

        return BuildTranscodedCommand(request, runtimeExecutablePath);
    }

    private FFmpegCommand BuildStreamCopyCommand(VideoTrimExportRequest request, string runtimeExecutablePath)
    {
        IFFmpegCommandBuilder builder = CreateBaseBuilder(request, runtimeExecutablePath, useFastSeek: true)
            .AddParameter("-c", "copy");

        return request.OutputFormat.Extension.ToLowerInvariant() switch
        {
            ".m4v" => builder
                .AddParameter("-f", "mp4")
                .Build(),
            ".ts" => builder
                .AddParameter("-f", "mpegts")
                .Build(),
            ".m2ts" => builder
                .AddParameter("-f", "mpegts")
                .AddParameter("-mpegts_m2ts_mode", "1")
                .Build(),
            ".mpeg" => builder
                .AddParameter("-f", "mpeg")
                .Build(),
            ".mpg" => builder
                .AddParameter("-f", "mpeg")
                .Build(),
            _ => builder.Build()
        };
    }

    private FFmpegCommand BuildTranscodedCommand(VideoTrimExportRequest request, string runtimeExecutablePath)
    {
        IFFmpegCommandBuilder builder = CreateBaseBuilder(request, runtimeExecutablePath, useFastSeek: false);

        return request.OutputFormat.Extension.ToLowerInvariant() switch
        {
            ".mp4" => BuildH264AacCommand(builder, request.VideoAccelerationKind, addFastStart: true),
            ".mkv" => BuildH264AacCommand(builder, request.VideoAccelerationKind),
            ".mov" => BuildH264AacCommand(builder, request.VideoAccelerationKind, addFastStart: true),
            ".m4v" => BuildH264AacCommand(builder, request.VideoAccelerationKind, formatOverride: "mp4", addFastStart: true),
            ".avi" => BuildAviCommand(builder),
            ".wmv" => BuildWmvCommand(builder),
            ".flv" => BuildFlvCommand(builder),
            ".webm" => BuildWebMCommand(builder),
            ".ts" => BuildH264AacCommand(builder, request.VideoAccelerationKind, formatOverride: "mpegts"),
            ".m2ts" => BuildH264AacCommand(builder, request.VideoAccelerationKind, formatOverride: "mpegts", enableM2TsMode: true),
            ".mpeg" => BuildMpegCommand(builder),
            ".mpg" => BuildMpegCommand(builder),
            _ => throw new InvalidOperationException("不支持的裁剪输出格式。")
        };
    }

    private IFFmpegCommandBuilder CreateBaseBuilder(
        VideoTrimExportRequest request,
        string runtimeExecutablePath,
        bool useFastSeek)
    {
        var builder = _commandBuilder
            .Reset()
            .SetExecutablePath(runtimeExecutablePath)
            .AddGlobalParameter("-hide_banner")
            .AddGlobalParameter(_configuration.OverwriteOutputFiles ? "-y" : "-n");

        if (useFastSeek)
        {
            builder = builder
                .AddGlobalParameter("-ss", FormatTimestamp(request.StartPosition))
                .AddGlobalParameter("-to", FormatTimestamp(request.EndPosition));
        }

        builder = builder
            .SetInput(request.InputPath)
            .SetOutput(request.OutputPath);

        if (!useFastSeek)
        {
            builder = builder
                .AddParameter("-ss", FormatTimestamp(request.StartPosition))
                .AddParameter("-t", FormatTimestamp(request.Duration));
        }

        return builder
            .AddParameter("-map", "0:v:0?")
            .AddParameter("-map", "0:a?")
            .AddParameter("-sn")
            .AddParameter("-dn");
    }

    private static FFmpegCommand BuildH264AacCommand(
        IFFmpegCommandBuilder builder,
        VideoAccelerationKind videoAccelerationKind,
        string? formatOverride = null,
        bool addFastStart = false,
        bool enableM2TsMode = false)
    {
        builder = ApplyVideoEncoding(builder, videoAccelerationKind)
            .AddParameter("-c:a", "aac")
            .AddParameter("-b:a", "256k");

        if (!string.IsNullOrWhiteSpace(formatOverride))
        {
            builder = builder.AddParameter("-f", formatOverride);
        }

        if (enableM2TsMode)
        {
            builder = builder.AddParameter("-mpegts_m2ts_mode", "1");
        }

        if (addFastStart)
        {
            builder = builder.AddParameter("-movflags", "+faststart");
        }

        return builder.Build();
    }

    private static FFmpegCommand BuildAviCommand(IFFmpegCommandBuilder builder) =>
        builder
            .AddParameter("-c:v", "mpeg4")
            .AddParameter("-q:v", "2")
            .AddParameter("-pix_fmt", "yuv420p")
            .AddParameter("-c:a", "libmp3lame")
            .AddParameter("-q:a", "2")
            .Build();

    private static FFmpegCommand BuildWmvCommand(IFFmpegCommandBuilder builder) =>
        builder
            .AddParameter("-c:v", "wmv2")
            .AddParameter("-b:v", "4M")
            .AddParameter("-pix_fmt", "yuv420p")
            .AddParameter("-c:a", "wmav2")
            .AddParameter("-b:a", "192k")
            .Build();

    private static FFmpegCommand BuildFlvCommand(IFFmpegCommandBuilder builder) =>
        builder
            .AddParameter("-c:v", "flv")
            .AddParameter("-b:v", "3M")
            .AddParameter("-pix_fmt", "yuv420p")
            .AddParameter("-c:a", "libmp3lame")
            .AddParameter("-b:a", "192k")
            .Build();

    private static FFmpegCommand BuildWebMCommand(IFFmpegCommandBuilder builder) =>
        builder
            .AddParameter("-c:v", "libvpx-vp9")
            .AddParameter("-crf", "30")
            .AddParameter("-b:v", "0")
            .AddParameter("-pix_fmt", "yuv420p")
            .AddParameter("-c:a", "libopus")
            .AddParameter("-b:a", "160k")
            .Build();

    private static FFmpegCommand BuildMpegCommand(IFFmpegCommandBuilder builder) =>
        builder
            .AddParameter("-c:v", "mpeg2video")
            .AddParameter("-q:v", "2")
            .AddParameter("-pix_fmt", "yuv420p")
            .AddParameter("-c:a", "mp2")
            .AddParameter("-b:a", "192k")
            .AddParameter("-f", "mpeg")
            .Build();

    private static IFFmpegCommandBuilder ApplyVideoEncoding(
        IFFmpegCommandBuilder builder,
        VideoAccelerationKind videoAccelerationKind) =>
        videoAccelerationKind switch
        {
            VideoAccelerationKind.NvidiaNvenc => builder
                .AddParameter("-c:v", "h264_nvenc")
                .AddParameter("-preset", "p5")
                .AddParameter("-cq", "23")
                .AddParameter("-pix_fmt", "yuv420p"),
            VideoAccelerationKind.IntelQuickSync => builder
                .AddParameter("-c:v", "h264_qsv")
                .AddParameter("-global_quality", "23")
                .AddParameter("-look_ahead", "0")
                .AddParameter("-pix_fmt", "nv12"),
            VideoAccelerationKind.AmdAmf => builder
                .AddParameter("-c:v", "h264_amf")
                .AddParameter("-quality", "quality")
                .AddParameter("-rc", "cqp")
                .AddParameter("-qp_i", "23")
                .AddParameter("-qp_p", "23")
                .AddParameter("-pix_fmt", "nv12"),
            _ => builder
                .AddParameter("-c:v", "libx264")
                .AddParameter("-crf", "23")
                .AddParameter("-preset", "medium")
                .AddParameter("-pix_fmt", "yuv420p")
        };

    private static bool SupportsFastContainerCopy(string inputPath, string outputExtension)
    {
        var inputExtension = Path.GetExtension(inputPath);
        if (string.IsNullOrWhiteSpace(inputExtension) || string.IsNullOrWhiteSpace(outputExtension))
        {
            return false;
        }

        return string.Equals(
            NormalizeContainerFamily(inputExtension),
            NormalizeContainerFamily(outputExtension),
            StringComparison.Ordinal);
    }

    private static string NormalizeContainerFamily(string extension) =>
        extension.Trim().ToLowerInvariant() switch
        {
            ".mp4" or ".m4v" => ".mp4",
            ".ts" or ".m2ts" => ".ts",
            ".mpeg" or ".mpg" => ".mpeg",
            _ => extension.Trim().ToLowerInvariant()
        };

    private static string FormatTimestamp(TimeSpan time)
    {
        var totalHours = (int)time.TotalHours;
        return FormattableString.Invariant($"{totalHours:00}:{time.Minutes:00}:{time.Seconds:00}.{time.Milliseconds:000}");
    }
}
