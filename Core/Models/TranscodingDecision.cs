namespace Vidvix.Core.Models;

public sealed record TranscodingDecision(
    TranscodingMode TranscodingMode,
    bool IsGpuAccelerationRequested,
    bool IsGpuApplicable,
    bool SupportsHardwareVideoEncoding,
    VideoAccelerationKind VideoAccelerationKind,
    LogLevel MessageLevel,
    string Message)
{
    public bool UsesHardwareVideoEncoding => VideoAccelerationKind != VideoAccelerationKind.None;
}
