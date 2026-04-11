using System;

namespace Vidvix.Core.Models;

public readonly record struct FFmpegProgressUpdate(
    TimeSpan? ProcessedDuration,
    TimeSpan? TotalDuration,
    double? ProgressRatio,
    bool IsCompleted);
