using System;
using System.Collections.ObjectModel;
using Vidvix.Core.Models;

namespace Vidvix.ViewModels;

internal sealed class MergeWorkspaceModeState
{
    public MergeWorkspaceModeState(
        MergeWorkspaceModeProfile profile,
        ObservableCollection<TrackItem> videoTrackItems,
        ObservableCollection<TrackItem> audioTrackItems)
    {
        Profile = profile ?? throw new ArgumentNullException(nameof(profile));
        VideoTrackItems = videoTrackItems ?? throw new ArgumentNullException(nameof(videoTrackItems));
        AudioTrackItems = audioTrackItems ?? throw new ArgumentNullException(nameof(audioTrackItems));
    }

    public MergeWorkspaceModeProfile Profile { get; }

    public ObservableCollection<TrackItem> VideoTrackItems { get; }

    public ObservableCollection<TrackItem> AudioTrackItems { get; }
}
