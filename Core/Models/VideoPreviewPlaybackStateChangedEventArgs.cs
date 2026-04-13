using System;

namespace Vidvix.Core.Models;

public sealed class VideoPreviewPlaybackStateChangedEventArgs : EventArgs
{
    public VideoPreviewPlaybackStateChangedEventArgs(bool isPlaying)
    {
        IsPlaying = isPlaying;
    }

    public bool IsPlaying { get; }
}
