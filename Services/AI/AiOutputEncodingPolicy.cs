using System;
using System.Collections.Generic;

namespace Vidvix.Services.AI;

internal static class AiOutputEncodingPolicy
{
    public static void ApplyEncoding(
        List<string> arguments,
        string outputExtension,
        bool includeAudio,
        bool transcodeAudio)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        switch (NormalizeExtension(outputExtension))
        {
            case ".mp4":
                ApplyH264Output(arguments, includeAudio, transcodeAudio, addFastStart: true);
                break;
            case ".mkv":
                ApplyH264Output(arguments, includeAudio, transcodeAudio);
                break;
            case ".mov":
                ApplyH264Output(arguments, includeAudio, transcodeAudio, addFastStart: true);
                break;
            case ".m4v":
                ApplyH264Output(arguments, includeAudio, transcodeAudio, formatOverride: "mp4", addFastStart: true);
                break;
            case ".ts":
                ApplyH264Output(arguments, includeAudio, transcodeAudio, formatOverride: "mpegts");
                break;
            case ".m2ts":
                ApplyH264Output(arguments, includeAudio, transcodeAudio, formatOverride: "mpegts", m2tsMode: true);
                break;
            case ".avi":
                AddRange(arguments, "-c:v", "mpeg4", "-q:v", "2", "-pix_fmt", "yuv420p");
                AppendAudio(arguments, includeAudio, transcodeAudio, "-c:a", "libmp3lame", "-q:a", "2");
                break;
            case ".wmv":
                AddRange(arguments, "-c:v", "wmv2", "-b:v", "4M", "-pix_fmt", "yuv420p");
                AppendAudio(arguments, includeAudio, transcodeAudio, "-c:a", "wmav2", "-b:a", "192k");
                break;
            case ".flv":
                AddRange(arguments, "-c:v", "flv", "-b:v", "3M", "-pix_fmt", "yuv420p");
                AppendAudio(arguments, includeAudio, transcodeAudio, "-c:a", "libmp3lame", "-b:a", "192k");
                break;
            case ".webm":
                AddRange(arguments, "-c:v", "libvpx-vp9", "-crf", "32", "-b:v", "0", "-pix_fmt", "yuv420p");
                AppendAudio(arguments, includeAudio, transcodeAudio, "-c:a", "libopus", "-b:a", "160k");
                break;
            case ".mpeg":
            case ".mpg":
                AddRange(arguments, "-c:v", "mpeg2video", "-q:v", "2", "-pix_fmt", "yuv420p", "-f", "mpeg");
                AppendAudio(arguments, includeAudio, transcodeAudio, "-c:a", "mp2", "-b:a", "192k");
                break;
            default:
                throw new InvalidOperationException("不支持的 AI 输出格式。");
        }
    }

    public static string GetAudioFallbackCodecDisplayName(string outputExtension) =>
        NormalizeExtension(outputExtension) switch
        {
            ".avi" or ".flv" => "MP3",
            ".wmv" => "WMA",
            ".webm" => "Opus",
            ".mpeg" or ".mpg" => "MP2",
            _ => "AAC"
        };

    private static void ApplyH264Output(
        List<string> arguments,
        bool includeAudio,
        bool transcodeAudio,
        string? formatOverride = null,
        bool addFastStart = false,
        bool m2tsMode = false)
    {
        AddRange(arguments, "-c:v", "libx264", "-preset", "medium", "-crf", "18", "-pix_fmt", "yuv420p");
        AppendAudio(arguments, includeAudio, transcodeAudio, "-c:a", "aac", "-b:a", "192k");

        if (!string.IsNullOrWhiteSpace(formatOverride))
        {
            AddRange(arguments, "-f", formatOverride);
        }

        if (m2tsMode)
        {
            AddRange(arguments, "-mpegts_m2ts_mode", "1");
        }

        if (addFastStart)
        {
            AddRange(arguments, "-movflags", "+faststart");
        }
    }

    private static void AppendAudio(
        List<string> arguments,
        bool includeAudio,
        bool transcodeAudio,
        params string[] transcodeArguments)
    {
        if (!includeAudio)
        {
            return;
        }

        if (transcodeAudio)
        {
            AddRange(arguments, transcodeArguments);
            return;
        }

        AddRange(arguments, "-c:a", "copy");
    }

    private static string NormalizeExtension(string extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
        {
            return string.Empty;
        }

        var trimmed = extension.Trim();
        return trimmed.StartsWith(".", StringComparison.Ordinal)
            ? trimmed.ToLowerInvariant()
            : $".{trimmed.ToLowerInvariant()}";
    }

    private static void AddRange(ICollection<string> arguments, params string[] values)
    {
        foreach (var value in values)
        {
            arguments.Add(value);
        }
    }
}
