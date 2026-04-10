using System;

namespace Vidvix.Core.Models;

public sealed class TranscodingModeOption
{
    public TranscodingModeOption(TranscodingMode mode, string displayName, string description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);

        Mode = mode;
        DisplayName = displayName;
        Description = description;
    }

    public TranscodingMode Mode { get; }

    public string DisplayName { get; }

    public string Description { get; }
}
