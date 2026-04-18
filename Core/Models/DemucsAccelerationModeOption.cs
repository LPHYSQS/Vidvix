using System;

namespace Vidvix.Core.Models;

public sealed class DemucsAccelerationModeOption
{
    public DemucsAccelerationModeOption(
        DemucsAccelerationMode mode,
        string displayName,
        string description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);

        Mode = mode;
        DisplayName = displayName;
        Description = description;
    }

    public DemucsAccelerationMode Mode { get; }

    public string DisplayName { get; }

    public string Description { get; }
}
