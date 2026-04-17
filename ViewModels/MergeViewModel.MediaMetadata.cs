using System;
using System.IO;
using Vidvix.Core.Models;

namespace Vidvix.ViewModels;

public sealed partial class MergeViewModel
{
    private bool IsVideoByExtension(string filePath)
    {
        var extension = Path.GetExtension(filePath);
        if (string.IsNullOrWhiteSpace(extension))
        {
            return true;
        }

        return _supportedVideoInputFileTypes.Contains(extension) || !_supportedAudioInputFileTypes.Contains(extension);
    }

    private bool ResolveIsVideo(MediaDetailsSnapshot snapshot, string filePath)
    {
        if (snapshot.HasVideoStream)
        {
            return true;
        }

        if (snapshot.HasAudioStream)
        {
            return false;
        }

        return IsVideoByExtension(filePath);
    }

    private static string FormatDuration(TimeSpan duration) => duration.ToString(@"hh\:mm\:ss");

    private static bool TryParseTrackDuration(string durationText, out TimeSpan duration) =>
        TimeSpan.TryParse(durationText, out duration);

    private TrackItem CreateTrackItem(MediaItem mediaItem, int index, bool isSourceAvailable)
    {
        ArgumentNullException.ThrowIfNull(mediaItem);
        var visualWidth = mediaItem.IsVideo
            ? Math.Clamp(164d + (mediaItem.DurationSeconds * 2.2d), 248d, 360d)
            : Math.Clamp(148d + (mediaItem.DurationSeconds * 1.8d), 220d, 320d);

        return new TrackItem(
            mediaItem.FileName,
            mediaItem.SourcePath,
            mediaItem.DurationText,
            mediaItem.DurationSeconds,
            mediaItem.ResolutionText,
            visualWidth,
            mediaItem.IsVideo,
            index,
            isSourceAvailable,
            ResolveMediaItemHasEmbeddedAudio(mediaItem));
    }

    private bool ResolveMediaItemHasEmbeddedAudio(MediaItem mediaItem)
    {
        ArgumentNullException.ThrowIfNull(mediaItem);
        if (!mediaItem.IsVideo ||
            _mediaInfoService is null ||
            string.IsNullOrWhiteSpace(mediaItem.SourcePath))
        {
            return false;
        }

        return _mediaInfoService.TryGetCachedDetails(mediaItem.SourcePath, out var snapshot) &&
               snapshot.HasAudioStream;
    }
}
