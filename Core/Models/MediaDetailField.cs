namespace Vidvix.Core.Models;

public sealed class MediaDetailField
{
    public string? Key { get; init; }

    public required string Label { get; init; }

    public required string Value { get; init; }
}
