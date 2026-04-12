using System;

namespace Vidvix.Core.Models;

public sealed record VideoTrimExportRequest(
    string InputPath,
    string OutputPath,
    TimeSpan StartPosition,
    TimeSpan EndPosition,
    OutputFormatOption OutputFormat)
{
    public TimeSpan Duration => EndPosition - StartPosition;
}
