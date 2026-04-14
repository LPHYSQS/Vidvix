using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Vidvix.Services.MediaInfo;

public sealed partial class MediaInfoService
{
    private static string FormatContainer(string? formatLongName, string? formatName)
    {
        if (!string.IsNullOrWhiteSpace(formatLongName))
        {
            return formatLongName;
        }

        if (string.IsNullOrWhiteSpace(formatName))
        {
            return UnknownValue;
        }

        var primaryName = formatName.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
        return string.IsNullOrWhiteSpace(primaryName) ? UnknownValue : primaryName.ToUpperInvariant();
    }

    private static string FormatCodec(string? codecName)
    {
        if (string.IsNullOrWhiteSpace(codecName))
        {
            return UnknownValue;
        }

        return codecName.ToLowerInvariant() switch
        {
            "h264" => "H.264 / AVC",
            "hevc" => "H.265 / HEVC",
            "av1" => "AV1",
            "vp9" => "VP9",
            "mpeg4" => "MPEG-4 Part 2",
            "prores" => "ProRes",
            "aac" => "AAC",
            "mp3" => "MP3",
            "ac3" => "AC-3",
            "eac3" => "E-AC-3",
            "flac" => "FLAC",
            "opus" => "Opus",
            "vorbis" => "Vorbis",
            "wmav2" => "WMA v2",
            "truehd" => "Dolby TrueHD",
            "dts" => "DTS",
            _ when codecName.StartsWith("pcm_", StringComparison.OrdinalIgnoreCase) => "PCM",
            _ => codecName.ToUpperInvariant()
        };
    }

    private static string BuildProfileLevel(string? profile, int? level)
    {
        var profileText = string.IsNullOrWhiteSpace(profile) ? UnknownValue : profile;
        var levelText = FormatLevel(level);

        if (profileText == UnknownValue && levelText == UnknownValue)
        {
            return UnknownValue;
        }

        if (levelText == UnknownValue)
        {
            return profileText;
        }

        if (profileText == UnknownValue)
        {
            return levelText;
        }

        return profileText + " / " + levelText;
    }

    private static string FormatDuration(string? durationText) =>
        FormatDuration(ParseDurationSeconds(durationText));

    private static string FormatDuration(double? durationSeconds)
    {
        if (durationSeconds is not >= 0)
        {
            return UnknownValue;
        }

        var duration = TimeSpan.FromSeconds(durationSeconds.Value);
        if (duration.TotalHours >= 1)
        {
            return $"{(int)duration.TotalHours:00}:{duration.Minutes:00}:{duration.Seconds:00}";
        }

        if (duration.TotalMinutes >= 1)
        {
            return $"{duration.Minutes:00}:{duration.Seconds:00}";
        }

        return $"{Math.Max(duration.TotalSeconds, 0.1):F1} 秒";
    }

    private static string FormatResolution(int? width, int? height)
    {
        if (width is not > 0 || height is not > 0)
        {
            return UnknownValue;
        }

        return $"{width} x {height}";
    }

    private static string FormatFrameRate(string? averageFrameRate, string? realFrameRate)
    {
        var frameRate = ParseFrameRate(averageFrameRate) ?? ParseFrameRate(realFrameRate);
        return frameRate is null ? UnknownValue : $"{frameRate.Value:0.###} 帧/秒";
    }

    private static double? ParseFrameRate(string? rawFrameRate)
    {
        if (string.IsNullOrWhiteSpace(rawFrameRate))
        {
            return null;
        }

        var segments = rawFrameRate.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length == 2 &&
            double.TryParse(segments[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var numerator) &&
            double.TryParse(segments[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var denominator) &&
            denominator > 0)
        {
            return numerator / denominator;
        }

        return double.TryParse(rawFrameRate, NumberStyles.Float, CultureInfo.InvariantCulture, out var frameRate)
            ? frameRate
            : null;
    }

    private static string FormatBitrate(string? bitrateText)
    {
        if (!double.TryParse(bitrateText, NumberStyles.Float, CultureInfo.InvariantCulture, out var bitsPerSecond) || bitsPerSecond <= 0)
        {
            return UnknownValue;
        }

        if (bitsPerSecond >= 1_000_000)
        {
            return $"{bitsPerSecond / 1_000_000d:0.##} Mbps";
        }

        if (bitsPerSecond >= 1_000)
        {
            return $"{bitsPerSecond / 1_000d:0.##} kbps";
        }

        return $"{bitsPerSecond:0} 比特/秒";
    }

    private static string FormatBitDepth(string? bitsPerRawSample, string? pixelFormat)
    {
        if (int.TryParse(bitsPerRawSample, NumberStyles.Integer, CultureInfo.InvariantCulture, out var bitDepth) && bitDepth > 0)
        {
            return bitDepth + " 位";
        }

        if (string.IsNullOrWhiteSpace(pixelFormat))
        {
            return UnknownValue;
        }

        var normalized = pixelFormat.ToLowerInvariant();
        if (normalized.Contains("p16", StringComparison.Ordinal)) return "16 位";
        if (normalized.Contains("p14", StringComparison.Ordinal)) return "14 位";
        if (normalized.Contains("p12", StringComparison.Ordinal)) return "12 位";
        if (normalized.Contains("p10", StringComparison.Ordinal)) return "10 位";
        if (normalized.Contains("p9", StringComparison.Ordinal)) return "9 位";
        return "8 位";
    }

    private static string DetermineHdrType(string? colorTransfer)
    {
        if (string.Equals(colorTransfer, "smpte2084", StringComparison.OrdinalIgnoreCase))
        {
            return "HDR10";
        }

        if (string.Equals(colorTransfer, "arib-std-b67", StringComparison.OrdinalIgnoreCase))
        {
            return "HLG";
        }

        return "SDR";
    }

    private static string FormatLevel(int? level)
    {
        if (level is not > 0)
        {
            return UnknownValue;
        }

        return level.Value >= 10 && level.Value <= 99
            ? (level.Value / 10d).ToString("0.#", CultureInfo.InvariantCulture)
            : level.Value.ToString(CultureInfo.InvariantCulture);
    }

    private static string FormatSampleRate(string? sampleRateText)
    {
        if (!double.TryParse(sampleRateText, NumberStyles.Float, CultureInfo.InvariantCulture, out var sampleRate) || sampleRate <= 0)
        {
            return UnknownValue;
        }

        return sampleRate >= 1_000
            ? $"{sampleRate / 1_000d:0.###} kHz"
            : $"{sampleRate:0} Hz";
    }

    private static string FormatChannels(string? channelLayout, int? channels)
    {
        var friendlyLayout = channelLayout?.ToLowerInvariant() switch
        {
            "mono" => "单声道",
            "stereo" => "立体声",
            "2.1" => "2.1 声道",
            "3.0" => "3.0 声道",
            "4.0" => "4.0 声道",
            "5.1" => "5.1 声道",
            "5.1(side)" => "5.1 声道",
            "7.1" => "7.1 声道",
            "7.1(wide)" => "7.1 声道",
            _ => channelLayout
        };

        if (!string.IsNullOrWhiteSpace(friendlyLayout) && channels is > 0)
        {
            return $"{friendlyLayout}（{channels} 声道）";
        }

        if (!string.IsNullOrWhiteSpace(friendlyLayout))
        {
            return friendlyLayout;
        }

        return channels is > 0 ? $"{channels} 声道" : UnknownValue;
    }

    private static string DeriveChromaSubsampling(string? pixelFormat)
    {
        if (string.IsNullOrWhiteSpace(pixelFormat))
        {
            return UnknownValue;
        }

        var normalized = pixelFormat.ToLowerInvariant();
        if (normalized.Contains("444", StringComparison.Ordinal)) return "4:4:4";
        if (normalized.Contains("422", StringComparison.Ordinal)) return "4:2:2";
        if (normalized.Contains("420", StringComparison.Ordinal)) return "4:2:0";
        if (normalized.Contains("440", StringComparison.Ordinal)) return "4:4:0";
        if (normalized.Contains("411", StringComparison.Ordinal)) return "4:1:1";
        if (normalized.Contains("410", StringComparison.Ordinal)) return "4:1:0";
        if (normalized.Contains("mono", StringComparison.Ordinal)) return "4:0:0";
        return UnknownValue;
    }

    private static string ResolveEncoderTag(FfprobeFormat? format, FfprobeStream? videoStream, FfprobeStream? audioStream)
    {
        return FirstNonEmpty(
                   GetTagValue(format?.tags, "encoder"),
                   GetTagValue(format?.tags, "ENCODER"),
                   GetTagValue(videoStream?.tags, "encoder"),
                   GetTagValue(videoStream?.tags, "ENCODER"),
                   GetTagValue(audioStream?.tags, "encoder"),
                   GetTagValue(audioStream?.tags, "ENCODER"),
                   videoStream?.codec_tag_string,
                   audioStream?.codec_tag_string) ??
               UnknownValue;
    }

    private static string? GetTagValue(Dictionary<string, string>? tags, string key)
    {
        if (tags is null)
        {
            return null;
        }

        return tags.TryGetValue(key, out var value) ? value : null;
    }

    private static string NormalizeValue(string? value) =>
        string.IsNullOrWhiteSpace(value) ? UnknownValue : value;

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
}
