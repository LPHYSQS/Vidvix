using System;

namespace Vidvix.Core.Models;

public sealed record VideoJoinSegment(
    string SourcePath,
    string SourceName,
    int Width,
    int Height,
    double FrameRate,
    TimeSpan Duration,
    bool HasAudio);
