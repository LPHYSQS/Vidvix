using System;

namespace Vidvix.Core.Models;

public sealed class ProcessingModeOption
{
    public ProcessingModeOption(ProcessingMode mode, string displayName, string description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);

        Mode = mode;
        DisplayName = displayName;
        Description = description;
    }

    public ProcessingMode Mode { get; }

    public string DisplayName { get; }

    public string Description { get; }
}
