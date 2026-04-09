using System;
using Vidvix.Core.Interfaces;
using Vidvix.Core.Models;

namespace Vidvix.ViewModels;

public sealed partial class MainViewModel
{
    // 命令构建和错误消息提炼单独放置，方便未来替换为更多模式策略。

    private FFmpegCommand BuildCommand(string inputPath, string outputPath)
    {
        if (string.IsNullOrWhiteSpace(_runtimeExecutablePath))
        {
            throw new InvalidOperationException("运行环境尚未准备完成。");
        }

        IFFmpegCommandBuilder builder = _ffmpegCommandBuilder
            .Reset()
            .SetExecutablePath(_runtimeExecutablePath)
            .AddGlobalParameter("-hide_banner")
            .SetInput(inputPath)
            .SetOutput(outputPath);

        builder = builder.AddGlobalParameter(_configuration.OverwriteOutputFiles ? "-y" : "-n");

        return SelectedProcessingMode.Mode switch
        {
            ProcessingMode.VideoConvert => BuildVideoOutputCommand(
                builder
                    .AddParameter("-map", "0:v")
                    .AddParameter("-map", "0:a?")
                    .AddParameter("-sn")
                    .AddParameter("-dn"),
                includeAudio: true),
            ProcessingMode.VideoTrackExtract => BuildVideoOutputCommand(
                builder
                    .AddParameter("-map", "0:v")
                    .AddParameter("-an")
                    .AddParameter("-sn")
                    .AddParameter("-dn"),
                includeAudio: false),
            ProcessingMode.AudioTrackExtract => BuildAudioExtractionCommand(builder),
            _ => throw new InvalidOperationException("不支持的处理模式。")
        };
    }

    private FFmpegCommand BuildVideoOutputCommand(IFFmpegCommandBuilder builder, bool includeAudio)
    {
        var extension = SelectedOutputFormat.Extension.ToLowerInvariant();

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

    private FFmpegCommand BuildAudioExtractionCommand(IFFmpegCommandBuilder builder)
    {
        builder = builder
            .AddParameter("-map", "0:a:0")
            .AddParameter("-vn")
            .AddParameter("-sn")
            .AddParameter("-dn");

        var extension = SelectedOutputFormat.Extension.ToLowerInvariant();

        builder = extension switch
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
            _ => throw new InvalidOperationException("不支持的音频输出格式。")
        };

        return builder.Build();
    }
}
