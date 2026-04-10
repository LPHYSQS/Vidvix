namespace Vidvix.Core.Models;

public sealed class VideoAccelerationProbeResult
{
    public VideoAccelerationKind Kind { get; init; }

    public string DisplayName { get; init; } = "CPU";

    public string EncoderName { get; init; } = string.Empty;

    public string Message { get; init; } = string.Empty;

    public bool IsAvailable => Kind != VideoAccelerationKind.None;
}
