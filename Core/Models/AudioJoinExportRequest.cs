using System;
using System.Collections.Generic;
using System.Linq;

namespace Vidvix.Core.Models;

public sealed record AudioJoinExportRequest(
    IReadOnlyList<AudioJoinSegment> Segments,
    string OutputPath,
    OutputFormatOption OutputFormat,
    TranscodingMode TranscodingMode,
    bool IsGpuAccelerationRequested,
    AudioJoinParameterMode ParameterMode,
    int TargetSampleRate,
    int? TargetBitrate)
{
    public TimeSpan TotalDuration => Segments.Aggregate(TimeSpan.Zero, static (current, segment) => current + segment.Duration);
}
