using System;
using System.IO;

namespace Vidvix.Services.FFmpeg;

internal static class TranscodingCompatibilityEvaluator
{
    public static bool CanCopyAudioCodecToContainer(string? codecName, string outputExtension)
    {
        var normalizedCodec = NormalizeCodecName(codecName);
        var normalizedExtension = NormalizeExtension(outputExtension);
        if (string.IsNullOrWhiteSpace(normalizedCodec) || string.IsNullOrWhiteSpace(normalizedExtension))
        {
            return false;
        }

        return normalizedExtension switch
        {
            ".mp3" => normalizedCodec == "mp3",
            ".aac" => normalizedCodec == "aac",
            ".m4a" => normalizedCodec == "aac",
            ".flac" => normalizedCodec == "flac",
            ".wav" => normalizedCodec == "pcm_s16le",
            ".aif" or ".aiff" => normalizedCodec == "pcm_s16be",
            ".opus" => normalizedCodec == "opus",
            ".ogg" => normalizedCodec == "vorbis",
            ".wma" => normalizedCodec == "wmav2",
            ".mka" => normalizedCodec is
                "aac" or
                "ac3" or
                "dts" or
                "eac3" or
                "flac" or
                "mp3" or
                "opus" or
                "pcm_s16be" or
                "pcm_s16le" or
                "vorbis",
            _ => false
        };
    }

    public static bool CanCopyVideoCodecToContainer(string? codecName, string outputExtension)
    {
        var normalizedCodec = NormalizeCodecName(codecName);
        var normalizedExtension = NormalizeExtension(outputExtension);
        if (string.IsNullOrWhiteSpace(normalizedCodec) || string.IsNullOrWhiteSpace(normalizedExtension))
        {
            return false;
        }

        return normalizedExtension switch
        {
            ".mp4" or ".m4v" or ".mov" => normalizedCodec is "h264" or "hevc",
            ".mkv" => normalizedCodec is "av1" or "h264" or "hevc" or "mpeg2video" or "mpeg4" or "vp8" or "vp9",
            ".ts" or ".m2ts" => normalizedCodec is "h264" or "hevc" or "mpeg2video",
            ".webm" => normalizedCodec is "av1" or "vp8" or "vp9",
            _ => false
        };
    }

    public static bool SupportsFastVideoTrimCopy(string inputPath, string outputExtension)
    {
        var inputExtension = Path.GetExtension(inputPath);
        return !string.IsNullOrWhiteSpace(inputExtension) &&
               string.Equals(
                   NormalizeContainerFamily(inputExtension),
                   NormalizeContainerFamily(outputExtension),
                   StringComparison.Ordinal);
    }

    public static bool AreFrameRatesCompatible(double left, double right, double tolerance = 0.02d) =>
        left > 0d &&
        right > 0d &&
        Math.Abs(left - right) <= tolerance;

    public static bool AreChannelLayoutsCompatible(string? left, string? right) =>
        string.Equals(NormalizeChannelLayout(left), NormalizeChannelLayout(right), StringComparison.Ordinal);

    public static string NormalizeContainerFamily(string extension) =>
        NormalizeExtension(extension) switch
        {
            ".mp4" or ".m4v" => ".mp4",
            ".ts" or ".m2ts" => ".ts",
            ".mpeg" or ".mpg" => ".mpeg",
            var value => value
        };

    private static string NormalizeCodecName(string? codecName) =>
        (codecName ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "h265" => "hevc",
            "x264" => "h264",
            var value => value
        };

    private static string NormalizeExtension(string? extension)
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

    private static string NormalizeChannelLayout(string? channelLayout) =>
        string.IsNullOrWhiteSpace(channelLayout)
            ? string.Empty
            : channelLayout.Trim().ToLowerInvariant();
}
