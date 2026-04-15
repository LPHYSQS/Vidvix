using System;

namespace Vidvix.Core.Models;

public sealed record AudioJoinSegment(
    string SourcePath,
    string SourceName,
    TimeSpan Duration,
    int SampleRate,
    int? Bitrate);
