using System;
using System.Collections.Generic;

namespace Vidvix.Services.MediaInfo;

public sealed partial class MediaInfoService
{
    private readonly record struct CachedMediaDetails(
        MediaCacheContext CacheContext,
        FfprobeResponse ProbeResult,
        ResolvedStreamBitrates ResolvedBitrates);

    private readonly record struct ResolvedStreamBitrates(
        string? VideoBitrateText,
        string? AudioBitrateText);

    private readonly record struct MediaCacheContext(
        string InputPath,
        string NormalizedPath,
        string FileName,
        DateTime LastWriteTimeUtc,
        string CacheKey);

    private readonly record struct FfprobeExecutionResult(
        int ExitCode,
        string StandardOutput,
        string StandardError);

    private sealed class FfprobeResponse
    {
        public FfprobeFormat? format { get; init; }

        public IReadOnlyList<FfprobeStream>? streams { get; init; }
    }

    private sealed class FfprobeFormat
    {
        public string? duration { get; init; }

        public string? bit_rate { get; init; }

        public string? format_name { get; init; }

        public string? format_long_name { get; init; }

        public Dictionary<string, string>? tags { get; init; }
    }

    private sealed class FfprobeStream
    {
        public string? codec_type { get; init; }

        public string? codec_name { get; init; }

        public string? profile { get; init; }

        public int? level { get; init; }

        public int? width { get; init; }

        public int? height { get; init; }

        public string? avg_frame_rate { get; init; }

        public string? r_frame_rate { get; init; }

        public string? duration { get; init; }

        public string? bit_rate { get; init; }

        public string? bits_per_raw_sample { get; init; }

        public int? bits_per_sample { get; init; }

        public string? pix_fmt { get; init; }

        public string? color_space { get; init; }

        public string? color_primaries { get; init; }

        public string? color_transfer { get; init; }

        public int? channels { get; init; }

        public string? channel_layout { get; init; }

        public string? sample_rate { get; init; }

        public string? codec_tag_string { get; init; }

        public Dictionary<string, string>? tags { get; init; }

        public FfprobeDisposition? disposition { get; init; }
    }

    private sealed class FfprobeDisposition
    {
        public int? attached_pic { get; init; }
    }
}
