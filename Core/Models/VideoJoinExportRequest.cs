using System;
using System.Collections.Generic;
using System.Linq;

namespace Vidvix.Core.Models;

public sealed record VideoJoinExportRequest(
    IReadOnlyList<VideoJoinSegment> Segments,
    string OutputPath,
    OutputFormatOption OutputFormat,
    int PresetWidth,
    int PresetHeight,
    double PresetFrameRate,
    MergeSmallerResolutionStrategy SmallerResolutionStrategy,
    MergeLargerResolutionStrategy LargerResolutionStrategy)
{
    public TimeSpan TotalDuration => Segments.Aggregate(TimeSpan.Zero, static (current, segment) => current + segment.Duration);

    public bool IncludeAudio => Segments.Any(static segment => segment.HasAudio);
}
