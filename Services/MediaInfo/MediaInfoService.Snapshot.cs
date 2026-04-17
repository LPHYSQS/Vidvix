using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Vidvix.Core.Models;

namespace Vidvix.Services.MediaInfo;

public sealed partial class MediaInfoService
{
    private static MediaDetailsSnapshot BuildSnapshot(MediaCacheContext cacheContext, FfprobeResponse probeResult, ResolvedStreamBitrates resolvedBitrates)
    {
        var format = probeResult.format;
        var streams = probeResult.streams ?? Array.Empty<FfprobeStream>();
        var videoStreams = streams.Where(IsVideoStream).ToArray();
        var videoStream = videoStreams.FirstOrDefault(stream => !IsAttachedPictureStream(stream));
        var artworkStream = videoStreams.FirstOrDefault(IsAttachedPictureStream);
        var audioStream = streams.FirstOrDefault(IsAudioStream);
        var subtitleStreams = streams
            .Where(IsSubtitleStream)
            .ToArray();
        var subtitleStream = subtitleStreams.FirstOrDefault();
        var mediaDurationSeconds = FirstPositive(
            ParseDurationSeconds(format?.duration),
            ResolveStreamDurationSeconds(videoStream, format),
            ResolveStreamDurationSeconds(audioStream, format),
            ResolveStreamDurationSeconds(subtitleStream, format));

        var resolutionText = FormatResolution(videoStream?.width, videoStream?.height);
        var videoProfileLevel = BuildProfileLevel(videoStream?.profile, videoStream?.level);
        var hdrType = DetermineHdrType(videoStream?.color_transfer);
        var encoderTag = ResolveEncoderTag(format, videoStream, audioStream);
        var videoMissing = videoStream is null;
        var audioMissing = audioStream is null;
        var hasEmbeddedArtwork = artworkStream is not null;
        var subtitleCount = subtitleStreams.Length;
        var primaryVideoFrameRate = ParseFrameRate(videoStream?.avg_frame_rate) ?? ParseFrameRate(videoStream?.r_frame_rate);
        var primaryAudioSampleRate = TryParsePositiveDouble(audioStream?.sample_rate, out var sampleRate)
            ? (int?)Math.Round(sampleRate, MidpointRounding.AwayFromZero)
            : null;
        var primaryAudioChannelLayout = !string.IsNullOrWhiteSpace(audioStream?.channel_layout)
            ? audioStream.channel_layout
            : audioStream?.channels is > 0
                ? $"{audioStream.channels.Value}ch"
                : null;
        var videoBitrateText = videoMissing ? MissingVideoStreamValue : FormatBitrate(resolvedBitrates.VideoBitrateText);
        var audioBitrateText = audioMissing ? MissingAudioStreamValue : FormatBitrate(resolvedBitrates.AudioBitrateText);
        var overviewFields = new List<MediaDetailField>
        {
            new() { Label = "文件名", Value = cacheContext.FileName },
            new() { Label = "时长", Value = FormatDuration(mediaDurationSeconds) },
            new() { Label = "总码率", Value = FormatBitrate(format?.bit_rate) },
            new() { Label = "封装格式", Value = FormatContainer(format?.format_long_name, format?.format_name) },
            new() { Label = "字幕轨道", Value = subtitleCount > 0 ? $"{subtitleCount} 条" : "未检测到字幕轨道" }
        };

        if (!videoMissing)
        {
            overviewFields.Insert(2, new MediaDetailField { Label = "分辨率", Value = resolutionText });
        }

        var videoFields = videoMissing
            ? Array.Empty<MediaDetailField>()
            : new[]
            {
                new MediaDetailField { Label = "编码", Value = FormatCodec(videoStream?.codec_name) },
                new MediaDetailField { Label = "规格 / 级别", Value = videoProfileLevel },
                new MediaDetailField { Label = "分辨率", Value = resolutionText },
                new MediaDetailField { Label = "帧率", Value = FormatFrameRate(videoStream?.avg_frame_rate, videoStream?.r_frame_rate) },
                new MediaDetailField { Label = "视频码率", Value = videoBitrateText },
                new MediaDetailField { Label = "色深", Value = FormatBitDepth(videoStream?.bits_per_raw_sample, videoStream?.pix_fmt) },
                new MediaDetailField { Label = "像素格式", Value = NormalizeValue(videoStream?.pix_fmt) },
                new MediaDetailField { Label = "色彩空间", Value = NormalizeValue(videoStream?.color_space) },
                new MediaDetailField { Label = "色域", Value = NormalizeValue(videoStream?.color_primaries) },
                new MediaDetailField { Label = "HDR 类型", Value = hdrType }
            };

        var audioFields = audioMissing
            ? Array.Empty<MediaDetailField>()
            : new[]
            {
                new MediaDetailField { Label = "编码", Value = FormatCodec(audioStream?.codec_name) },
                new MediaDetailField { Label = "声道", Value = FormatChannels(audioStream?.channel_layout, audioStream?.channels) },
                new MediaDetailField { Label = "采样率", Value = FormatSampleRate(audioStream?.sample_rate) },
                new MediaDetailField { Label = "音频码率", Value = audioBitrateText }
            };

        var advancedFields = videoMissing
            ? Array.Empty<MediaDetailField>()
            : new[]
            {
                new MediaDetailField { Label = "色度抽样", Value = DeriveChromaSubsampling(videoStream?.pix_fmt) },
                new MediaDetailField { Label = "传输特性", Value = NormalizeValue(videoStream?.color_transfer) },
                new MediaDetailField { Label = "编码器标记", Value = encoderTag }
            };

        return new MediaDetailsSnapshot
        {
            InputPath = cacheContext.InputPath,
            FileName = cacheContext.FileName,
            LastWriteTimeUtc = cacheContext.LastWriteTimeUtc,
            MediaDuration = mediaDurationSeconds is > 0
                ? TimeSpan.FromSeconds(mediaDurationSeconds.Value)
                : null,
            HasVideoStream = !videoMissing,
            HasAudioStream = !audioMissing,
            HasEmbeddedArtwork = hasEmbeddedArtwork,
            HasSubtitleStream = subtitleCount > 0,
            SubtitleStreamCount = subtitleCount,
            PrimaryVideoCodecName = videoStream?.codec_name,
            PrimaryAudioCodecName = audioStream?.codec_name,
            PrimarySubtitleCodecName = subtitleStream?.codec_name,
            PrimaryVideoFrameRate = primaryVideoFrameRate is > 0d ? primaryVideoFrameRate : null,
            PrimaryAudioSampleRate = primaryAudioSampleRate is > 0 ? primaryAudioSampleRate : null,
            PrimaryAudioChannelLayout = primaryAudioChannelLayout,
            PrimaryVideoWidth = videoStream?.width is > 0 ? videoStream.width : null,
            PrimaryVideoHeight = videoStream?.height is > 0 ? videoStream.height : null,
            OverviewFields = overviewFields,
            VideoFields = videoFields,
            AudioFields = audioFields,
            AdvancedFields = advancedFields
        };
    }

    private static ResolvedStreamBitrates ResolveStreamBitrates(FfprobeResponse probeResult)
    {
        var streams = probeResult.streams ?? Array.Empty<FfprobeStream>();
        var format = probeResult.format;
        var videoStream = streams.FirstOrDefault(stream => IsVideoStream(stream) && !IsAttachedPictureStream(stream));
        var audioStream = streams.FirstOrDefault(IsAudioStream);

        var videoBitrateText = ResolveStreamBitrateText(videoStream, format, audioStream is null, isAudioStream: false);
        var audioBitrateText = ResolveStreamBitrateText(audioStream, format, videoStream is null, isAudioStream: true);

        return new ResolvedStreamBitrates(videoBitrateText, audioBitrateText);
    }

    private static string? ResolveStreamBitrateText(FfprobeStream? stream, FfprobeFormat? format, bool isOnlyPrimaryStream, bool isAudioStream)
    {
        if (stream is null)
        {
            return null;
        }

        var directBitrate = NormalizeNumericText(
            FirstNonEmpty(
                stream.bit_rate,
                GetTagValueIgnoreCase(stream.tags, "BPS", "BPS-eng", "variant_bitrate", "BANDWIDTH")));
        if (!string.IsNullOrWhiteSpace(directBitrate))
        {
            return directBitrate;
        }

        if (TryResolveBitrateFromTaggedBytes(stream, format, out var derivedBitrate))
        {
            return derivedBitrate;
        }

        if (isAudioStream && TryResolvePcmAudioBitrate(stream, out var pcmBitrate))
        {
            return pcmBitrate;
        }

        if (isOnlyPrimaryStream)
        {
            return NormalizeNumericText(format?.bit_rate);
        }

        return null;
    }

    private static bool TryResolveBitrateFromTaggedBytes(FfprobeStream stream, FfprobeFormat? format, out string bitrateText)
    {
        bitrateText = string.Empty;
        var durationSeconds = ResolveStreamDurationSeconds(stream, format);
        if (!TryParsePositiveDouble(GetTagValueIgnoreCase(stream.tags, "NUMBER_OF_BYTES", "NUMBER_OF_BYTES-eng"), out var bytes) ||
            durationSeconds is not > 0)
        {
            return false;
        }

        var bitsPerSecond = (bytes * 8d) / durationSeconds.Value;
        if (bitsPerSecond <= 0)
        {
            return false;
        }

        bitrateText = bitsPerSecond.ToString("0.###", CultureInfo.InvariantCulture);
        return true;
    }

    private static bool TryResolvePcmAudioBitrate(FfprobeStream stream, out string bitrateText)
    {
        bitrateText = string.Empty;

        if (string.IsNullOrWhiteSpace(stream.codec_name) ||
            !stream.codec_name.StartsWith("pcm_", StringComparison.OrdinalIgnoreCase) ||
            !TryParsePositiveDouble(stream.sample_rate, out var sampleRate) ||
            stream.channels is not > 0 ||
            stream.bits_per_sample is not > 0)
        {
            return false;
        }

        var bitsPerSecond = sampleRate * stream.channels.Value * stream.bits_per_sample.Value;
        if (bitsPerSecond <= 0)
        {
            return false;
        }

        bitrateText = bitsPerSecond.ToString("0.###", CultureInfo.InvariantCulture);
        return true;
    }

    private static string? NormalizeNumericText(string? value)
    {
        return TryParsePositiveDouble(value, out var numericValue)
            ? numericValue.ToString("0.###", CultureInfo.InvariantCulture)
            : null;
    }

    private static bool TryParsePositiveDouble(string? value, out double parsedValue)
    {
        parsedValue = 0;
        return !string.IsNullOrWhiteSpace(value) &&
               double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out parsedValue) &&
               parsedValue > 0;
    }

    private static double? ResolveStreamDurationSeconds(FfprobeStream? stream, FfprobeFormat? format)
    {
        return FirstPositive(
            ParseDurationSeconds(stream?.duration),
            ParseDurationSeconds(GetTagValueIgnoreCase(stream?.tags, "DURATION", "DURATION-eng")),
            ParseDurationSeconds(format?.duration));
    }

    private static double? ParseDurationSeconds(string? durationText)
    {
        if (TryParsePositiveDouble(durationText, out var seconds))
        {
            return seconds;
        }

        if (string.IsNullOrWhiteSpace(durationText))
        {
            return null;
        }

        var segments = durationText.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length != 3 ||
            !double.TryParse(segments[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var hours) ||
            !double.TryParse(segments[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var minutes) ||
            !double.TryParse(segments[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var remainingSeconds))
        {
            return null;
        }

        var totalSeconds = (hours * 3600d) + (minutes * 60d) + remainingSeconds;
        return totalSeconds > 0 ? totalSeconds : null;
    }

    private static double? FirstPositive(params double?[] values) =>
        values.FirstOrDefault(value => value is > 0);

    private static string? GetTagValueIgnoreCase(Dictionary<string, string>? tags, params string[] keys)
    {
        if (tags is null || keys.Length == 0)
        {
            return null;
        }

        foreach (var key in keys)
        {
            foreach (var entry in tags)
            {
                if (string.Equals(entry.Key, key, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(entry.Value))
                {
                    return entry.Value;
                }
            }
        }

        return null;
    }

    private static bool IsVideoStream(FfprobeStream stream) =>
        string.Equals(stream.codec_type, "video", StringComparison.OrdinalIgnoreCase);

    private static bool IsAudioStream(FfprobeStream stream) =>
        string.Equals(stream.codec_type, "audio", StringComparison.OrdinalIgnoreCase);

    private static bool IsSubtitleStream(FfprobeStream stream) =>
        string.Equals(stream.codec_type, "subtitle", StringComparison.OrdinalIgnoreCase);

    private static bool IsAttachedPictureStream(FfprobeStream stream) =>
        IsVideoStream(stream) &&
        stream.disposition?.attached_pic == 1;
}
