using System;
using System.Collections.Generic;

namespace Vidvix.Core.Models;

public sealed class MediaDetailsSnapshot
{
    public required string InputPath { get; init; }

    public required string FileName { get; init; }

    public required DateTime LastWriteTimeUtc { get; init; }

    public required TimeSpan? MediaDuration { get; init; }

    public required bool HasVideoStream { get; init; }

    public required bool HasAudioStream { get; init; }

    public required bool HasEmbeddedArtwork { get; init; }

    public required bool HasSubtitleStream { get; init; }

    public required int SubtitleStreamCount { get; init; }

    public string? PrimaryVideoCodecName { get; init; }

    public string? PrimaryAudioCodecName { get; init; }

    public string? PrimarySubtitleCodecName { get; init; }

    public double? PrimaryVideoFrameRate { get; init; }

    public int? PrimaryAudioSampleRate { get; init; }

    public string? PrimaryAudioChannelLayout { get; init; }

    public int? PrimaryVideoWidth { get; init; }

    public int? PrimaryVideoHeight { get; init; }

    public required IReadOnlyList<MediaDetailField> OverviewFields { get; init; }

    public required IReadOnlyList<MediaDetailField> VideoFields { get; init; }

    public required IReadOnlyList<MediaDetailField> AudioFields { get; init; }

    public required IReadOnlyList<MediaDetailField> AdvancedFields { get; init; }
}
