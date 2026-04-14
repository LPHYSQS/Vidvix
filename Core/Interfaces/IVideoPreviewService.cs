using System;
using System.Threading;
using System.Threading.Tasks;
using Vidvix.Core.Models;

namespace Vidvix.Core.Interfaces;

public interface IVideoPreviewService : IDisposable
{
    event EventHandler<VideoPreviewMediaOpenedEventArgs>? MediaOpened;

    event EventHandler<VideoPreviewFailedEventArgs>? MediaFailed;

    event EventHandler<VideoPreviewPositionChangedEventArgs>? PositionChanged;

    event EventHandler<VideoPreviewPlaybackStateChangedEventArgs>? PlaybackStateChanged;

    event EventHandler? MediaEnded;

    bool HasLoadedMedia { get; }

    bool IsPlaying { get; }

    TimeSpan Duration { get; }

    TimeSpan CurrentPosition { get; }

    void UpdateHostPlacement(VideoPreviewHostPlacement placement);

    Task InitializeAsync(CancellationToken cancellationToken = default);

    Task RefreshAsync(CancellationToken cancellationToken = default);

    Task LoadAsync(
        string inputPath,
        double volume,
        bool enableExternalSubtitleAutoLoad = true,
        CancellationToken cancellationToken = default);

    Task UnloadAsync(CancellationToken cancellationToken = default);

    Task PlayAsync(CancellationToken cancellationToken = default);

    Task<TimeSpan> PauseAsync(CancellationToken cancellationToken = default);

    Task<TimeSpan> SeekAsync(TimeSpan position, CancellationToken cancellationToken = default);

    Task<TimeSpan> SetPlaybackPositionAsync(TimeSpan position, CancellationToken cancellationToken = default);

    Task SetVolumeAsync(double volume, CancellationToken cancellationToken = default);
}
