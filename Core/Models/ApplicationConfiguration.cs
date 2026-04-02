using System;
using System.Collections.Generic;

namespace Vidvix.Core.Models;

public sealed class ApplicationConfiguration
{
    public string FFmpegExecutablePath { get; init; } = "ffmpeg";

    public string OutputAudioExtension { get; init; } = ".mp3";

    public bool OverwriteOutputFiles { get; init; } = true;

    public bool MirrorLogsToConsole { get; init; } = true;

    public TimeSpan? DefaultExecutionTimeout { get; init; }

    public IReadOnlyList<string> SupportedInputFileTypes { get; init; } =
        new[] { ".mp4", ".mov", ".mkv", ".avi", ".wmv", ".m4v" };
}

