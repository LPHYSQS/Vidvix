using System;

namespace Vidvix.Core.Models;

public sealed record VideoTrimExportRequest(
    string InputPath,
    string OutputPath,
    TimeSpan StartPosition,
    TimeSpan EndPosition,
    OutputFormatOption OutputFormat,
    TranscodingMode TranscodingMode,
    VideoAccelerationKind VideoAccelerationKind)
{
    public TimeSpan Duration => EndPosition - StartPosition;
}
