using System;
using Vidvix.Core.Interfaces;
using Vidvix.Core.Models;

namespace Vidvix.ViewModels;

public sealed partial class MainViewModel
{
    // 命令构建和错误消息提炼单独放置，方便未来替换为更多模式策略。

    private FFmpegCommand BuildCommand(string inputPath, string outputPath) =>
        BuildCommand(
            inputPath,
            outputPath,
            _selectedWorkspaceKind,
            IsAudioWorkspace ? ProcessingMode.AudioTrackExtract : SelectedProcessingMode.Mode,
            SelectedOutputFormat,
            new ProcessingExecutionContext(
                _selectedWorkspaceKind,
                IsAudioWorkspace ? ProcessingMode.AudioTrackExtract : SelectedProcessingMode.Mode,
                SelectedOutputFormat,
                SelectedTranscodingModeOption.Mode,
                EnableGpuAccelerationForTranscoding,
                VideoAccelerationKind.None));

    private FFmpegCommand BuildCommand(
        string inputPath,
        string outputPath,
        ProcessingWorkspaceKind workspaceKind,
        ProcessingMode processingMode,
        OutputFormatOption outputFormat) =>
        BuildCommand(
            inputPath,
            outputPath,
            workspaceKind,
            processingMode,
            outputFormat,
            new ProcessingExecutionContext(
                workspaceKind,
                processingMode,
                outputFormat,
                SelectedTranscodingModeOption.Mode,
                EnableGpuAccelerationForTranscoding,
                VideoAccelerationKind.None));

    private FFmpegCommand BuildCommand(
        string inputPath,
        string outputPath,
        ProcessingWorkspaceKind workspaceKind,
        ProcessingMode processingMode,
        OutputFormatOption outputFormat,
        ProcessingExecutionContext executionContext)
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

        if (workspaceKind == ProcessingWorkspaceKind.Audio)
        {
            return BuildAudioConversionCommand(builder, outputFormat, executionContext);
        }

        return processingMode switch
        {
            ProcessingMode.VideoConvert => BuildVideoOutputCommand(
                builder
                    .AddParameter("-map", "0:v")
                    .AddParameter("-map", "0:a?")
                    .AddParameter("-sn")
                    .AddParameter("-dn"),
                true,
                outputFormat,
                executionContext),
            ProcessingMode.VideoTrackExtract => BuildVideoOutputCommand(
                builder
                    .AddParameter("-map", "0:v")
                    .AddParameter("-an")
                    .AddParameter("-sn")
                    .AddParameter("-dn"),
                false,
                outputFormat,
                executionContext),
            ProcessingMode.AudioTrackExtract => BuildAudioExtractionCommand(builder, outputFormat, executionContext),
            _ => throw new InvalidOperationException("不支持的处理模式。")
        };
    }

    private FFmpegCommand BuildVideoOutputCommand(
        IFFmpegCommandBuilder builder,
        bool includeAudio,
        OutputFormatOption outputFormat,
        ProcessingExecutionContext executionContext)
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
        ProcessingExecutionContext executionContext) =>
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
        ProcessingExecutionContext executionContext) =>
        BuildAudioOutputCommand(
            builder
                .AddParameter("-map", "0:a:0")
                .AddParameter("-vn")
                .AddParameter("-sn")
                .AddParameter("-dn"),
            outputFormat,
            executionContext);

    private FFmpegCommand BuildAudioOutputCommand(
        IFFmpegCommandBuilder builder,
        OutputFormatOption outputFormat,
        ProcessingExecutionContext executionContext)
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

    private static bool CanUseHardwareVideoEncoding(OutputFormatOption outputFormat)
    {
        var extension = outputFormat.Extension.ToLowerInvariant();
        return extension is ".mp4" or ".mkv" or ".mov" or ".m4v" or ".ts" or ".m2ts";
    }
}
