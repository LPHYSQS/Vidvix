using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Vidvix.Core.Models;

internal static class MergeMediaMetadataParser
{
    public static bool TryResolveVideoJoinResolution(
        MediaDetailsSnapshot? snapshot,
        TrackItem trackItem,
        out int width,
        out int height)
    {
        if (snapshot?.PrimaryVideoWidth is > 0 && snapshot.PrimaryVideoHeight is > 0)
        {
            width = snapshot.PrimaryVideoWidth.Value;
            height = snapshot.PrimaryVideoHeight.Value;
            return true;
        }

        var resolutionText = snapshot is null
            ? trackItem.ResolutionText
            : TryGetDetailFieldValue(snapshot.VideoFields, "mediaDetails.field.resolution", "分辨率") ??
              TryGetDetailFieldValue(snapshot.OverviewFields, "mediaDetails.field.resolution", "分辨率") ??
              trackItem.ResolutionText;

        return TryParseResolutionText(resolutionText, out width, out height);
    }

    public static bool TryResolveVideoFrameRate(MediaDetailsSnapshot? snapshot, out double frameRate)
    {
        if (snapshot?.PrimaryVideoFrameRate is > 0d)
        {
            frameRate = snapshot.PrimaryVideoFrameRate.Value;
            return true;
        }

        frameRate = 0d;
        if (snapshot is null)
        {
            return false;
        }

        var frameRateText = TryGetDetailFieldValue(snapshot.VideoFields, "mediaDetails.field.frameRate", "帧率");
        if (string.IsNullOrWhiteSpace(frameRateText))
        {
            return false;
        }

        var numericText = new string(frameRateText
            .Trim()
            .TakeWhile(character => char.IsDigit(character) || character is '.')
            .ToArray());
        return double.TryParse(numericText, NumberStyles.Float, CultureInfo.InvariantCulture, out frameRate) &&
               frameRate > 0d;
    }

    public static bool TryResolveAudioJoinSampleRate(
        MediaDetailsSnapshot? snapshot,
        TrackItem trackItem,
        out int sampleRate)
    {
        if (snapshot?.PrimaryAudioSampleRate is > 0)
        {
            sampleRate = snapshot.PrimaryAudioSampleRate.Value;
            return true;
        }

        var sampleRateText = snapshot is null
            ? trackItem.ResolutionText
            : TryGetDetailFieldValue(snapshot.AudioFields, "mediaDetails.field.sampleRate", "采样率") ??
              trackItem.ResolutionText;

        return TryParseSampleRateText(sampleRateText, out sampleRate);
    }

    public static bool TryResolveAudioJoinBitrate(
        MediaDetailsSnapshot? snapshot,
        TrackItem trackItem,
        out int bitrate)
    {
        var bitrateText = snapshot is null
            ? trackItem.ResolutionText
            : TryGetDetailFieldValue(snapshot.AudioFields, "mediaDetails.field.audioBitrate", "音频码率") ??
              trackItem.ResolutionText;

        return TryParseBitrateText(bitrateText, out bitrate);
    }

    public static bool TryResolveAudioChannelLayout(MediaDetailsSnapshot? snapshot, out string channelLayout)
    {
        channelLayout = snapshot?.PrimaryAudioChannelLayout?.Trim() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(channelLayout);
    }

    public static string? ResolveVideoCodecName(MediaDetailsSnapshot? snapshot) =>
        string.IsNullOrWhiteSpace(snapshot?.PrimaryVideoCodecName)
            ? null
            : snapshot.PrimaryVideoCodecName;

    public static string? ResolveAudioCodecName(MediaDetailsSnapshot? snapshot) =>
        string.IsNullOrWhiteSpace(snapshot?.PrimaryAudioCodecName)
            ? null
            : snapshot.PrimaryAudioCodecName;

    public static string ResolveResolutionText(MediaDetailsSnapshot snapshot, string unknownResolutionText = "未知分辨率")
    {
        var resolutionText = snapshot.PrimaryVideoWidth is > 0 && snapshot.PrimaryVideoHeight is > 0
            ? $"{snapshot.PrimaryVideoWidth} x {snapshot.PrimaryVideoHeight}"
            : TryGetDetailFieldValue(snapshot.VideoFields, "mediaDetails.field.resolution", "分辨率") ??
              TryGetDetailFieldValue(snapshot.OverviewFields, "mediaDetails.field.resolution", "分辨率");
        return string.IsNullOrWhiteSpace(resolutionText) ? unknownResolutionText : resolutionText;
    }

    public static string ResolveAudioParameterText(MediaDetailsSnapshot snapshot, string unknownAudioParameterText = "未知音频参数")
    {
        var sampleRateText = snapshot.PrimaryAudioSampleRate is > 0
            ? snapshot.PrimaryAudioSampleRate >= 1_000
                ? $"{snapshot.PrimaryAudioSampleRate / 1_000d:0.###} kHz"
                : $"{snapshot.PrimaryAudioSampleRate:0} Hz"
            : TryGetDetailFieldValue(snapshot.AudioFields, "mediaDetails.field.sampleRate", "采样率");
        var bitrateText = TryGetDetailFieldValue(snapshot.AudioFields, "mediaDetails.field.audioBitrate", "音频码率");
        if (string.IsNullOrWhiteSpace(sampleRateText) && string.IsNullOrWhiteSpace(bitrateText))
        {
            return unknownAudioParameterText;
        }

        if (string.IsNullOrWhiteSpace(sampleRateText))
        {
            return bitrateText!;
        }

        if (string.IsNullOrWhiteSpace(bitrateText))
        {
            return sampleRateText;
        }

        return $"{sampleRateText} · {bitrateText}";
    }

    public static string? TryGetDetailFieldValue(
        IReadOnlyList<MediaDetailField> fields,
        string key,
        string? fallbackLabel = null)
    {
        foreach (var field in fields)
        {
            var matchesKey = !string.IsNullOrWhiteSpace(key) &&
                             string.Equals(field.Key, key, StringComparison.OrdinalIgnoreCase);
            var matchesLabel = !string.IsNullOrWhiteSpace(fallbackLabel) &&
                               string.Equals(field.Label, fallbackLabel, StringComparison.OrdinalIgnoreCase);
            if ((matchesKey || matchesLabel) &&
                !string.IsNullOrWhiteSpace(field.Value))
            {
                return field.Value;
            }
        }

        return null;
    }

    private static bool TryParseResolutionText(string? resolutionText, out int width, out int height)
    {
        width = 0;
        height = 0;
        if (string.IsNullOrWhiteSpace(resolutionText))
        {
            return false;
        }

        var normalizedText = resolutionText
            .Replace("×", "x", StringComparison.Ordinal)
            .Replace("X", "x", StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal);
        var parts = normalizedText.Split('x', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length == 2 &&
               int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out width) &&
               int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out height) &&
               width > 0 &&
               height > 0;
    }

    private static bool TryParseSampleRateText(string? sampleRateText, out int sampleRate)
    {
        sampleRate = 0;
        if (string.IsNullOrWhiteSpace(sampleRateText))
        {
            return false;
        }

        var segments = sampleRateText.Split(
            new[] { '·', '|', ',', ';', '，', '；' },
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var candidate = segments.FirstOrDefault(segment =>
            segment.Contains("Hz", StringComparison.OrdinalIgnoreCase));
        if (string.IsNullOrWhiteSpace(candidate))
        {
            candidate = sampleRateText;
        }

        var numericText = new string(candidate
            .Where(character => char.IsDigit(character) || character is '.')
            .ToArray());
        if (!double.TryParse(numericText, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || value <= 0d)
        {
            return false;
        }

        if (candidate.Contains("kHz", StringComparison.OrdinalIgnoreCase))
        {
            value *= 1_000d;
        }

        sampleRate = (int)Math.Round(value, MidpointRounding.AwayFromZero);
        return sampleRate > 0;
    }

    private static bool TryParseBitrateText(string? bitrateText, out int bitrate)
    {
        bitrate = 0;
        if (string.IsNullOrWhiteSpace(bitrateText))
        {
            return false;
        }

        var segments = bitrateText.Split(
            new[] { '·', '|', ',', ';', '，', '；' },
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var candidate = segments.FirstOrDefault(segment =>
            segment.Contains("bps", StringComparison.OrdinalIgnoreCase) ||
            segment.Contains("bits/s", StringComparison.OrdinalIgnoreCase) ||
            segment.Contains("比特/秒", StringComparison.Ordinal));
        if (string.IsNullOrWhiteSpace(candidate))
        {
            candidate = bitrateText;
        }

        var numericText = new string(candidate
            .Where(character => char.IsDigit(character) || character is '.')
            .ToArray());
        if (!double.TryParse(numericText, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || value <= 0d)
        {
            return false;
        }

        if (candidate.Contains("Mbps", StringComparison.OrdinalIgnoreCase))
        {
            value *= 1_000_000d;
        }
        else if (candidate.Contains("kbps", StringComparison.OrdinalIgnoreCase))
        {
            value *= 1_000d;
        }

        bitrate = (int)Math.Round(value, MidpointRounding.AwayFromZero);
        return bitrate > 0;
    }
}
