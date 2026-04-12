using System;

namespace Vidvix.Core.Models;

public sealed class MediaDetailsLoadResult
{
    private MediaDetailsLoadResult()
    {
    }

    public MediaDetailsSnapshot? Snapshot { get; private init; }

    public string? ErrorMessage { get; private init; }

    public string? DiagnosticDetails { get; private init; }

    public bool IsToolMissing { get; private init; }

    public bool IsSuccess => Snapshot is not null && string.IsNullOrWhiteSpace(ErrorMessage);

    public static MediaDetailsLoadResult Success(MediaDetailsSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return new MediaDetailsLoadResult
        {
            Snapshot = snapshot
        };
    }

    public static MediaDetailsLoadResult Failure(
        string errorMessage,
        string? diagnosticDetails = null,
        bool isToolMissing = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorMessage);
        return new MediaDetailsLoadResult
        {
            ErrorMessage = errorMessage,
            DiagnosticDetails = diagnosticDetails,
            IsToolMissing = isToolMissing
        };
    }
}
