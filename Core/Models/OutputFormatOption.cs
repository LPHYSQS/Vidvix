using System;

namespace Vidvix.Core.Models;

public sealed class OutputFormatOption
{
    public OutputFormatOption(string displayName, string extension, string description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(extension);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);

        DisplayName = displayName;
        Extension = extension.StartsWith(".", StringComparison.Ordinal)
            ? extension
            : $".{extension}";
        Description = description;
    }

    public string DisplayName { get; }

    public string Extension { get; }

    public string Description { get; }
}
