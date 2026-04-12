using System;
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

        IFFmpegCommandBuilder builder = _commandBuilder
            .Reset()
            .SetExecutablePath(runtimeExecutablePath)
            .AddGlobalParameter("-hide_banner")
            .SetInput(request.InputPath)
            .SetOutput(request.OutputPath)
            .AddGlobalParameter(_configuration.OverwriteOutputFiles ? "-y" : "-n")
            .AddParameter("-ss", FormatTimestamp(request.StartPosition))
            .AddParameter("-t", FormatTimestamp(request.Duration))
            .AddParameter("-map", "0:v:0?")
            .AddParameter("-map", "0:a?")
            .AddParameter("-sn")
            .AddParameter("-dn");

        return request.OutputFormat.Extension.ToLowerInvariant() switch
        {
            ".mp4" => BuildH264AacCommand(builder, addFastStart: true),
            ".mkv" => BuildH264AacCommand(builder),
            ".mov" => BuildH264AacCommand(builder, addFastStart: true),
            ".m4v" => BuildH264AacCommand(builder, formatOverride: "mp4", addFastStart: true),
            ".avi" => BuildAviCommand(builder),
            ".wmv" => BuildWmvCommand(builder),
            ".flv" => BuildFlvCommand(builder),
            ".webm" => BuildWebMCommand(builder),
            ".ts" => BuildH264AacCommand(builder, formatOverride: "mpegts"),
            ".m2ts" => BuildH264AacCommand(builder, formatOverride: "mpegts", enableM2TsMode: true),
            ".mpeg" => BuildMpegCommand(builder),
            ".mpg" => BuildMpegCommand(builder),
            _ => throw new InvalidOperationException("不支持的裁剪输出格式。")
        };
    }

    private static FFmpegCommand BuildH264AacCommand(
        IFFmpegCommandBuilder builder,
        string? formatOverride = null,
        bool addFastStart = false,
        bool enableM2TsMode = false)
    {
        builder = builder
            .AddParameter("-c:v", "libx264")
            .AddParameter("-preset", "medium")
            .AddParameter("-crf", "18")
            .AddParameter("-pix_fmt", "yuv420p")
            .AddParameter("-c:a", "aac")
            .AddParameter("-b:a", "192k");

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

    private static string FormatTimestamp(TimeSpan time)
    {
        var totalHours = (int)time.TotalHours;
        return FormattableString.Invariant($"{totalHours:00}:{time.Minutes:00}:{time.Seconds:00}.{time.Milliseconds:000}");
    }
}
