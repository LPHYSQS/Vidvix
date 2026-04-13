// 功能：媒体处理命令工厂（按处理模式与输出格式生成 FFmpeg 命令）
// 模块：视频转换模块 / 音频转换模块
// 说明：可复用，仅负责命令拼装策略，不涉及 UI。
using System;
using Vidvix.Core.Interfaces;
using Vidvix.Core.Models;

namespace Vidvix.Services.FFmpeg;

/// <summary>
/// 统一封装 FFmpeg 命令构建策略，让输出格式规则、编码规则和硬件编码规则不再滞留在 ViewModel 中。
/// </summary>
public sealed class MediaProcessingCommandFactory : IMediaProcessingCommandFactory
{
    private readonly ApplicationConfiguration _configuration;
    private readonly IFFmpegCommandBuilder _commandBuilder;

    public MediaProcessingCommandFactory(
        ApplicationConfiguration configuration,
        IFFmpegCommandBuilder commandBuilder)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _commandBuilder = commandBuilder ?? throw new ArgumentNullException(nameof(commandBuilder));
    }

    public FFmpegCommand Create(MediaProcessingCommandRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var context = request.Context;
        IFFmpegCommandBuilder builder = _commandBuilder
            .Reset()
            .SetExecutablePath(request.RuntimeExecutablePath)
            .AddGlobalParameter("-hide_banner")
            .SetInput(request.InputPath)
            .SetOutput(request.OutputPath);

        builder = builder.AddGlobalParameter(_configuration.OverwriteOutputFiles ? "-y" : "-n");

        if (context.WorkspaceKind == ProcessingWorkspaceKind.Audio)
        {
            return BuildAudioConversionCommand(builder, context.OutputFormat, context);
        }

        return context.ProcessingMode switch
        {
            ProcessingMode.VideoConvert => BuildVideoOutputCommand(
                builder
                    .AddParameter("-map", "0:v")
                    .AddParameter("-map", "0:a?")
                    .AddParameter("-sn")
                    .AddParameter("-dn"),
                includeAudio: true,
                context.OutputFormat,
                context),
            ProcessingMode.VideoTrackExtract => BuildVideoOutputCommand(
                builder
                    .AddParameter("-map", "0:v")
                    .AddParameter("-an")
                    .AddParameter("-sn")
                    .AddParameter("-dn"),
                includeAudio: false,
                context.OutputFormat,
                context),
            ProcessingMode.AudioTrackExtract => BuildAudioExtractionCommand(builder, context.OutputFormat, context),
            ProcessingMode.SubtitleTrackExtract => BuildSubtitleExtractionCommand(builder, context.OutputFormat),
            _ => throw new InvalidOperationException("不支持的处理模式。")
        };
    }

    public bool SupportsHardwareVideoEncoding(OutputFormatOption outputFormat) =>
        FFmpegVideoEncodingPolicy.SupportsHardwareVideoEncoding(outputFormat);

    private FFmpegCommand BuildVideoOutputCommand(
        IFFmpegCommandBuilder builder,
        bool includeAudio,
        OutputFormatOption outputFormat,
        MediaProcessingContext executionContext)
    {
        var extension = outputFormat.Extension.ToLowerInvariant();

        if (executionContext.TranscodingMode == TranscodingMode.FullTranscode)
        {
            return BuildTranscodedVideoOutputCommand(builder, includeAudio, extension, executionContext.VideoAccelerationKind);
        }

        return extension switch
        {
            ".mp4" => builder
                .AddParameter("-c", "copy")
                .AddParameter("-movflags", "+faststart")
                .Build(),
            ".mkv" => builder
                .AddParameter("-c", "copy")
                .Build(),
            ".mov" => builder
                .AddParameter("-c", "copy")
                .AddParameter("-movflags", "+faststart")
                .Build(),
            ".avi" => BuildAviOutputCommand(builder, includeAudio),
            ".wmv" => BuildWmvOutputCommand(builder, includeAudio),
            ".m4v" => builder
                .AddParameter("-c", "copy")
                .AddParameter("-f", "mp4")
                .AddParameter("-movflags", "+faststart")
                .Build(),
            ".flv" => BuildFlvOutputCommand(builder, includeAudio),
            ".webm" => BuildWebMOutputCommand(builder, includeAudio),
            ".ts" => builder
                .AddParameter("-c", "copy")
                .AddParameter("-f", "mpegts")
                .Build(),
            ".m2ts" => builder
                .AddParameter("-c", "copy")
                .AddParameter("-f", "mpegts")
                .AddParameter("-mpegts_m2ts_mode", "1")
                .Build(),
            ".mpeg" => BuildMpegOutputCommand(builder, includeAudio),
            ".mpg" => BuildMpegOutputCommand(builder, includeAudio),
            _ => throw new InvalidOperationException("不支持的视频输出格式。")
        };
    }

    private FFmpegCommand BuildTranscodedVideoOutputCommand(
        IFFmpegCommandBuilder builder,
        bool includeAudio,
        string extension,
        VideoAccelerationKind videoAccelerationKind)
    {
        return extension switch
        {
            ".mp4" => BuildH264VideoOutputCommand(builder, includeAudio, videoAccelerationKind, addFastStart: true),
            ".mkv" => BuildH264VideoOutputCommand(builder, includeAudio, videoAccelerationKind),
            ".mov" => BuildH264VideoOutputCommand(builder, includeAudio, videoAccelerationKind, addFastStart: true),
            ".m4v" => BuildH264VideoOutputCommand(builder, includeAudio, videoAccelerationKind, formatOverride: "mp4", addFastStart: true),
            ".ts" => BuildH264VideoOutputCommand(builder, includeAudio, videoAccelerationKind, formatOverride: "mpegts"),
            ".m2ts" => BuildH264VideoOutputCommand(builder, includeAudio, videoAccelerationKind, formatOverride: "mpegts", m2tsMode: true),
            ".avi" => BuildAviOutputCommand(builder, includeAudio),
            ".wmv" => BuildWmvOutputCommand(builder, includeAudio),
            ".flv" => BuildFlvOutputCommand(builder, includeAudio),
            ".webm" => BuildWebMOutputCommand(builder, includeAudio),
            ".mpeg" => BuildMpegOutputCommand(builder, includeAudio),
            ".mpg" => BuildMpegOutputCommand(builder, includeAudio),
            _ => throw new InvalidOperationException("不支持的视频输出格式。")
        };
    }

    private static FFmpegCommand BuildH264VideoOutputCommand(
        IFFmpegCommandBuilder builder,
        bool includeAudio,
        VideoAccelerationKind videoAccelerationKind,
        string? formatOverride = null,
        bool addFastStart = false,
        bool m2tsMode = false)
    {
        builder = ApplyVideoEncoding(builder, videoAccelerationKind);

        if (includeAudio)
        {
            builder = builder
                .AddParameter("-c:a", "aac")
                .AddParameter("-b:a", "256k");
        }

        if (!string.IsNullOrWhiteSpace(formatOverride))
        {
            builder = builder.AddParameter("-f", formatOverride);
        }

        if (m2tsMode)
        {
            builder = builder.AddParameter("-mpegts_m2ts_mode", "1");
        }

        if (addFastStart)
        {
            builder = builder.AddParameter("-movflags", "+faststart");
        }

        return builder.Build();
    }

    private static FFmpegCommand BuildAviOutputCommand(IFFmpegCommandBuilder builder, bool includeAudio)
    {
        builder = builder
            .AddParameter("-c:v", "mpeg4")
            .AddParameter("-q:v", "2")
            .AddParameter("-pix_fmt", "yuv420p");

        if (includeAudio)
        {
            builder = builder
                .AddParameter("-c:a", "libmp3lame")
                .AddParameter("-q:a", "2");
        }

        return builder.Build();
    }

    private static FFmpegCommand BuildWmvOutputCommand(IFFmpegCommandBuilder builder, bool includeAudio)
    {
        builder = builder
            .AddParameter("-c:v", "wmv2")
            .AddParameter("-b:v", "4M")
            .AddParameter("-pix_fmt", "yuv420p");

        if (includeAudio)
        {
            builder = builder
                .AddParameter("-c:a", "wmav2")
                .AddParameter("-b:a", "192k");
        }

        return builder.Build();
    }

    private static FFmpegCommand BuildFlvOutputCommand(IFFmpegCommandBuilder builder, bool includeAudio)
    {
        builder = builder
            .AddParameter("-c:v", "flv")
            .AddParameter("-b:v", "3M")
            .AddParameter("-pix_fmt", "yuv420p");

        if (includeAudio)
        {
            builder = builder
                .AddParameter("-c:a", "libmp3lame")
                .AddParameter("-b:a", "192k");
        }

        return builder.Build();
    }

    private static FFmpegCommand BuildWebMOutputCommand(IFFmpegCommandBuilder builder, bool includeAudio)
    {
        builder = builder
            .AddParameter("-c:v", "libvpx-vp9")
            .AddParameter("-crf", "32")
            .AddParameter("-b:v", "0")
            .AddParameter("-pix_fmt", "yuv420p");

        if (includeAudio)
        {
            builder = builder
                .AddParameter("-c:a", "libopus")
                .AddParameter("-b:a", "160k");
        }

        return builder.Build();
    }

    private static FFmpegCommand BuildMpegOutputCommand(IFFmpegCommandBuilder builder, bool includeAudio)
    {
        builder = builder
            .AddParameter("-c:v", "mpeg2video")
            .AddParameter("-q:v", "2")
            .AddParameter("-pix_fmt", "yuv420p")
            .AddParameter("-f", "mpeg");

        if (includeAudio)
        {
            builder = builder
                .AddParameter("-c:a", "mp2")
                .AddParameter("-b:a", "192k");
        }

        return builder.Build();
    }

    private FFmpegCommand BuildAudioConversionCommand(
        IFFmpegCommandBuilder builder,
        OutputFormatOption outputFormat,
        MediaProcessingContext executionContext) =>
        BuildAudioOutputCommand(
            builder
                .AddParameter("-map", "0:a:0")
                .AddParameter("-vn")
                .AddParameter("-sn")
                .AddParameter("-dn"),
            outputFormat,
            executionContext);

    private FFmpegCommand BuildAudioExtractionCommand(
        IFFmpegCommandBuilder builder,
        OutputFormatOption outputFormat,
        MediaProcessingContext executionContext) =>
        BuildAudioOutputCommand(
            builder
                .AddParameter("-map", "0:a:0")
                .AddParameter("-vn")
                .AddParameter("-sn")
                .AddParameter("-dn"),
            outputFormat,
            executionContext);

    private static FFmpegCommand BuildSubtitleExtractionCommand(
        IFFmpegCommandBuilder builder,
        OutputFormatOption outputFormat)
    {
        builder = builder
            .AddParameter("-map", "0:s:0")
            .AddParameter("-vn")
            .AddParameter("-an")
            .AddParameter("-dn");

        return outputFormat.Extension.ToLowerInvariant() switch
        {
            ".srt" => builder
                .AddParameter("-c:s", "srt")
                .AddParameter("-f", "srt")
                .Build(),
            ".ass" => builder
                .AddParameter("-c:s", "ass")
                .AddParameter("-f", "ass")
                .Build(),
            ".ssa" => builder
                .AddParameter("-c:s", "ssa")
                .AddParameter("-f", "ass")
                .Build(),
            ".vtt" => builder
                .AddParameter("-c:s", "webvtt")
                .AddParameter("-f", "webvtt")
                .Build(),
            ".ttml" => builder
                .AddParameter("-c:s", "ttml")
                .AddParameter("-f", "ttml")
                .Build(),
            ".mks" => builder
                .AddParameter("-c:s", "copy")
                .AddParameter("-f", "matroska")
                .Build(),
            _ => throw new InvalidOperationException("不支持的字幕输出格式。")
        };
    }

    private static FFmpegCommand BuildAudioOutputCommand(
        IFFmpegCommandBuilder builder,
        OutputFormatOption outputFormat,
        MediaProcessingContext executionContext)
    {
        var extension = outputFormat.Extension.ToLowerInvariant();

        builder = executionContext.TranscodingMode == TranscodingMode.FullTranscode
            ? extension switch
            {
                ".mp3" => builder
                    .AddParameter("-c:a", "libmp3lame")
                    .AddParameter("-q:a", "2"),
                ".m4a" => builder
                    .AddParameter("-c:a", "aac")
                    .AddParameter("-b:a", "256k")
                    .AddParameter("-movflags", "+faststart"),
                ".aac" => builder
                    .AddParameter("-c:a", "aac")
                    .AddParameter("-b:a", "256k"),
                ".wav" => builder
                    .AddParameter("-c:a", "pcm_s16le"),
                ".flac" => builder
                    .AddParameter("-c:a", "flac"),
                ".wma" => builder
                    .AddParameter("-c:a", "wmav2")
                    .AddParameter("-b:a", "192k"),
                ".ogg" => builder
                    .AddParameter("-c:a", "libvorbis")
                    .AddParameter("-q:a", "5"),
                ".opus" => builder
                    .AddParameter("-c:a", "libopus")
                    .AddParameter("-b:a", "160k"),
                ".aiff" => builder
                    .AddParameter("-c:a", "pcm_s16be"),
                ".aif" => builder
                    .AddParameter("-c:a", "pcm_s16be"),
                ".mka" => builder
                    .AddParameter("-c:a", "flac")
                    .AddParameter("-f", "matroska"),
                _ => throw new InvalidOperationException("不支持的音频输出格式。")
            }
            : extension switch
            {
                ".mp3" => builder
                    .AddParameter("-c:a", "libmp3lame")
                    .AddParameter("-q:a", "2"),
                ".m4a" => builder
                    .AddParameter("-c:a", "aac")
                    .AddParameter("-b:a", "256k")
                    .AddParameter("-movflags", "+faststart"),
                ".aac" => builder
                    .AddParameter("-c:a", "aac")
                    .AddParameter("-b:a", "256k"),
                ".wav" => builder
                    .AddParameter("-c:a", "pcm_s16le"),
                ".flac" => builder
                    .AddParameter("-c:a", "flac"),
                ".wma" => builder
                    .AddParameter("-c:a", "wmav2")
                    .AddParameter("-b:a", "192k"),
                ".ogg" => builder
                    .AddParameter("-c:a", "libvorbis")
                    .AddParameter("-q:a", "5"),
                ".opus" => builder
                    .AddParameter("-c:a", "libopus")
                    .AddParameter("-b:a", "160k"),
                ".aiff" => builder
                    .AddParameter("-c:a", "pcm_s16be"),
                ".aif" => builder
                    .AddParameter("-c:a", "pcm_s16be"),
                ".mka" => builder
                    .AddParameter("-c:a", "copy")
                    .AddParameter("-f", "matroska"),
                _ => throw new InvalidOperationException("不支持的音频输出格式。")
            };

        return builder.Build();
    }

    private static IFFmpegCommandBuilder ApplyVideoEncoding(
        IFFmpegCommandBuilder builder,
        VideoAccelerationKind videoAccelerationKind) =>
        FFmpegVideoEncodingPolicy.ApplyH264Encoding(builder, videoAccelerationKind);
}
