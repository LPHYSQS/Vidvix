using System;

namespace Vidvix.Core.Models;

public sealed class VideoPreviewMediaOpenedEventArgs : EventArgs
{
    public VideoPreviewMediaOpenedEventArgs(string sourcePath, TimeSpan duration)
    {
        SourcePath = sourcePath;
        Duration = duration;
    }

    public string SourcePath { get; }

    public TimeSpan Duration { get; }
}
