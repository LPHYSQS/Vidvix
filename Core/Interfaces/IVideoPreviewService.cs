using System;
using System.Threading;
using System.Threading.Tasks;
using Vidvix.Core.Models;

namespace Vidvix.Core.Interfaces;

public interface IVideoPreviewService : IDisposable
{
    event EventHandler<VideoPreviewMediaOpenedEventArgs>? MediaOpened;

    event EventHandler<VideoPreviewFailedEventArgs>? MediaFailed;

    event EventHandler<VideoPreviewPlaybackStateChangedEventArgs>? PlaybackStateChanged;

    event EventHandler? MediaEnded;

    bool HasLoadedMedia { get; }

    bool IsPlaying { get; }

    TimeSpan Duration { get; }

    TimeSpan CurrentPosition { get; }

    void UpdateHostPlacement(VideoPreviewHostPlacement placement);

    Task LoadAsync(string inputPath, double volume, CancellationToken cancellationToken = default);

    void Unload();

    void Play();

    void Pause();

    void Seek(TimeSpan position);

    void SetPlaybackPosition(TimeSpan position);

    TimeSpan GetCurrentPosition();

    void SetVolume(double volume);
}
