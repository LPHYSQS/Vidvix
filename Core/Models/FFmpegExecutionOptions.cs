using System;

namespace Vidvix.Core.Models;

public sealed class FFmpegExecutionOptions
{
    public TimeSpan? Timeout { get; init; }

    public TimeSpan? InputDuration { get; init; }

    public IProgress<FFmpegProgressUpdate>? Progress { get; init; }
}
