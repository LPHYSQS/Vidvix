using System;
using System.Collections.Generic;
using System.Linq;

namespace Vidvix.Core.Models;

public sealed record AudioJoinExportRequest(
    IReadOnlyList<AudioJoinSegment> Segments,
    string OutputPath,
    OutputFormatOption OutputFormat,
    int PresetSampleRate,
    int? PresetBitrate)
{
    public TimeSpan TotalDuration => Segments.Aggregate(TimeSpan.Zero, static (current, segment) => current + segment.Duration);
}
