using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Vidvix.Core.Models;

namespace Vidvix.Core.Interfaces;

public interface IMergeMediaAnalysisService
{
    Task<IReadOnlyList<VideoJoinSegment>> BuildVideoJoinSegmentsAsync(
        IReadOnlyList<TrackItem> activeTrackItems,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AudioJoinSegment>> BuildAudioJoinSegmentsAsync(
        IReadOnlyList<TrackItem> activeTrackItems,
        CancellationToken cancellationToken = default);

    Task<AudioVideoComposeSourceAnalysis> AnalyzeAudioVideoComposeAsync(
        TrackItem videoTrackItem,
        TrackItem audioTrackItem,
        CancellationToken cancellationToken = default);
}
