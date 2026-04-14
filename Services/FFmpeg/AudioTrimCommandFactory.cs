using System;
using Vidvix.Core.Interfaces;
using Vidvix.Core.Models;

namespace Vidvix.Services.FFmpeg;

public sealed class AudioTrimCommandFactory : IAudioTrimCommandFactory
{
    private readonly ApplicationConfiguration _configuration;
    private readonly IFFmpegCommandBuilder _commandBuilder;

    public AudioTrimCommandFactory(
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

        var builder = _commandBuilder
            .Reset()
            .SetExecutablePath(runtimeExecutablePath)
            .AddGlobalParameter("-hide_banner")
            .AddGlobalParameter(_configuration.OverwriteOutputFiles ? "-y" : "-n")
            .SetInput(request.InputPath)
            .SetOutput(request.OutputPath)
            .AddParameter("-ss", FormatTimestamp(request.StartPosition))
            .AddParameter("-t", FormatTimestamp(request.Duration))
            .AddParameter("-map", "0:a:0")
            .AddParameter("-vn")
            .AddParameter("-sn")
            .AddParameter("-dn");

        return BuildAudioOutputCommand(builder, request.OutputFormat, request.TranscodingMode);
    }

    private static FFmpegCommand BuildAudioOutputCommand(
        IFFmpegCommandBuilder builder,
        OutputFormatOption outputFormat,
        TranscodingMode transcodingMode)
    {
        var extension = outputFormat.Extension.ToLowerInvariant();

        builder = transcodingMode == TranscodingMode.FullTranscode
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
                _ => throw new InvalidOperationException("不支持的音频裁剪输出格式。")
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
                _ => throw new InvalidOperationException("不支持的音频裁剪输出格式。")
            };

        return builder.Build();
    }

    private static string FormatTimestamp(TimeSpan time)
    {
        var totalHours = (int)time.TotalHours;
        return FormattableString.Invariant($"{totalHours:00}:{time.Minutes:00}:{time.Seconds:00}.{time.Milliseconds:000}");
    }
}
