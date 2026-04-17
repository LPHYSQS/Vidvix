using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Vidvix.Core.Interfaces;
using Vidvix.Core.Models;

namespace Vidvix.Services;

/// <summary>
/// 为合并模块提供素材分析与导出前分段准备，避免这些业务细节长期堆积在 ViewModel 中。
/// </summary>
public sealed class MergeMediaAnalysisService : IMergeMediaAnalysisService
{
    private readonly IMediaInfoService _mediaInfoService;
    private readonly ILogger _logger;

    public MergeMediaAnalysisService(IMediaInfoService mediaInfoService, ILogger logger)
    {
        _mediaInfoService = mediaInfoService ?? throw new ArgumentNullException(nameof(mediaInfoService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<IReadOnlyList<VideoJoinSegment>> BuildVideoJoinSegmentsAsync(
        IReadOnlyList<TrackItem> activeTrackItems,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(activeTrackItems);

        var segments = new List<VideoJoinSegment>(activeTrackItems.Count);

        foreach (var trackItem in activeTrackItems)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var snapshot = await LoadMediaDetailsSnapshotAsync(trackItem.SourcePath, cancellationToken);
            if (snapshot is not null && !snapshot.HasVideoStream)
            {
                throw new InvalidOperationException($"{trackItem.SourceName} 不包含可拼接的视频流。");
            }

            if (!MergeMediaMetadataParser.TryResolveVideoJoinResolution(snapshot, trackItem, out var width, out var height))
            {
                throw new InvalidOperationException($"无法读取 {trackItem.SourceName} 的分辨率信息。");
            }

            var duration = await ResolveTrackDurationAsync(trackItem.SourcePath, trackItem.DurationText, cancellationToken);
            if (duration <= TimeSpan.Zero)
            {
                throw new InvalidOperationException($"无法读取 {trackItem.SourceName} 的时长信息。");
            }

            var frameRate = MergeMediaMetadataParser.TryResolveVideoFrameRate(snapshot, out var resolvedFrameRate)
                ? resolvedFrameRate
                : 30d;

            segments.Add(new VideoJoinSegment(
                trackItem.SourcePath,
                trackItem.SourceName,
                width,
                height,
                frameRate,
                duration,
                snapshot?.HasAudioStream ?? true,
                MergeMediaMetadataParser.ResolveVideoCodecName(snapshot),
                MergeMediaMetadataParser.ResolveAudioCodecName(snapshot),
                snapshot?.PrimaryAudioSampleRate ?? 0,
                MergeMediaMetadataParser.TryResolveAudioChannelLayout(snapshot, out var audioChannelLayout)
                    ? audioChannelLayout
                    : null,
                Path.GetExtension(trackItem.SourcePath)));
        }

        return segments;
    }

    public async Task<IReadOnlyList<AudioJoinSegment>> BuildAudioJoinSegmentsAsync(
        IReadOnlyList<TrackItem> activeTrackItems,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(activeTrackItems);

        var segments = new List<AudioJoinSegment>(activeTrackItems.Count);

        foreach (var trackItem in activeTrackItems)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var snapshot = await LoadMediaDetailsSnapshotAsync(trackItem.SourcePath, cancellationToken);
            if (snapshot is not null && !snapshot.HasAudioStream)
            {
                throw new InvalidOperationException($"{trackItem.SourceName} 不包含可拼接的音频流。");
            }

            var duration = await ResolveTrackDurationAsync(trackItem.SourcePath, trackItem.DurationText, cancellationToken);
            if (duration <= TimeSpan.Zero)
            {
                throw new InvalidOperationException($"无法读取 {trackItem.SourceName} 的时长信息。");
            }

            if (!MergeMediaMetadataParser.TryResolveAudioJoinSampleRate(snapshot, trackItem, out var sampleRate))
            {
                throw new InvalidOperationException($"无法读取 {trackItem.SourceName} 的采样率信息。");
            }

            int? bitrate = MergeMediaMetadataParser.TryResolveAudioJoinBitrate(snapshot, trackItem, out var resolvedBitrate)
                ? resolvedBitrate
                : null;

            segments.Add(new AudioJoinSegment(
                trackItem.SourcePath,
                trackItem.SourceName,
                duration,
                sampleRate,
                bitrate,
                MergeMediaMetadataParser.ResolveAudioCodecName(snapshot),
                MergeMediaMetadataParser.TryResolveAudioChannelLayout(snapshot, out var audioChannelLayout)
                    ? audioChannelLayout
                    : null,
                Path.GetExtension(trackItem.SourcePath)));
        }

        return segments;
    }

    public async Task<AudioVideoComposeSourceAnalysis> AnalyzeAudioVideoComposeAsync(
        TrackItem videoTrackItem,
        TrackItem audioTrackItem,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(videoTrackItem);
        ArgumentNullException.ThrowIfNull(audioTrackItem);

        var videoSnapshot = await LoadMediaDetailsSnapshotAsync(videoTrackItem.SourcePath, cancellationToken);
        var audioSnapshot = await LoadMediaDetailsSnapshotAsync(audioTrackItem.SourcePath, cancellationToken);

        if (videoSnapshot is not null && !videoSnapshot.HasVideoStream)
        {
            throw new InvalidOperationException($"{videoTrackItem.SourceName} 不包含可用于合成的视频流。");
        }

        if (audioSnapshot is not null && !audioSnapshot.HasAudioStream)
        {
            throw new InvalidOperationException($"{audioTrackItem.SourceName} 不包含可用于合成的音频流。");
        }

        var videoDuration = await ResolveTrackDurationAsync(videoTrackItem.SourcePath, videoTrackItem.DurationText, cancellationToken);
        if (videoDuration <= TimeSpan.Zero)
        {
            throw new InvalidOperationException($"无法读取 {videoTrackItem.SourceName} 的时长信息。");
        }

        var audioDuration = await ResolveTrackDurationAsync(audioTrackItem.SourcePath, audioTrackItem.DurationText, cancellationToken);
        if (audioDuration <= TimeSpan.Zero)
        {
            throw new InvalidOperationException($"无法读取 {audioTrackItem.SourceName} 的时长信息。");
        }

        var frameRate = MergeMediaMetadataParser.TryResolveVideoFrameRate(videoSnapshot, out var resolvedFrameRate)
            ? resolvedFrameRate
            : 30d;

        return new AudioVideoComposeSourceAnalysis(
            videoDuration,
            audioDuration,
            frameRate,
            videoSnapshot?.HasAudioStream == true,
            MergeMediaMetadataParser.ResolveVideoCodecName(videoSnapshot),
            MergeMediaMetadataParser.ResolveAudioCodecName(audioSnapshot),
            Path.GetExtension(videoTrackItem.SourcePath),
            Path.GetExtension(audioTrackItem.SourcePath));
    }

    private async Task<MediaDetailsSnapshot?> LoadMediaDetailsSnapshotAsync(string sourcePath, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            return null;
        }

        if (_mediaInfoService.TryGetCachedDetails(sourcePath, out var cachedSnapshot))
        {
            return cachedSnapshot;
        }

        var loadResult = await _mediaInfoService.GetMediaDetailsAsync(sourcePath, cancellationToken);
        return loadResult.IsSuccess ? loadResult.Snapshot : null;
    }

    private async Task<TimeSpan> ResolveTrackDurationAsync(
        string sourcePath,
        string fallbackDurationText,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(sourcePath))
        {
            try
            {
                var loadResult = _mediaInfoService.TryGetCachedDetails(sourcePath, out var cachedSnapshot)
                    ? MediaDetailsLoadResult.Success(cachedSnapshot)
                    : await _mediaInfoService.GetMediaDetailsAsync(sourcePath, cancellationToken);

                if (loadResult.IsSuccess &&
                    loadResult.Snapshot?.MediaDuration is { } mediaDuration &&
                    mediaDuration > TimeSpan.Zero)
                {
                    return mediaDuration;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                _logger.Log(LogLevel.Warning, $"读取合并素材时长失败：{sourcePath}", exception);
            }
        }

        return TimeSpan.TryParse(fallbackDurationText, out var parsedDuration)
            ? parsedDuration
            : TimeSpan.Zero;
    }
}
