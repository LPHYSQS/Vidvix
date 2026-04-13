using System;

namespace Vidvix.Core.Models;

public sealed class VideoPreviewPositionChangedEventArgs : EventArgs
{
    public VideoPreviewPositionChangedEventArgs(TimeSpan position)
    {
        Position = position;
    }

    public TimeSpan Position { get; }
}
