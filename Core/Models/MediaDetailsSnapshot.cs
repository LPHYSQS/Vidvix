using System;
using System.Collections.Generic;

namespace Vidvix.Core.Models;

public sealed class MediaDetailsSnapshot
{
    public required string InputPath { get; init; }

    public required string FileName { get; init; }

    public required DateTime LastWriteTimeUtc { get; init; }

    public required IReadOnlyList<MediaDetailField> OverviewFields { get; init; }

    public required IReadOnlyList<MediaDetailField> VideoFields { get; init; }

    public required IReadOnlyList<MediaDetailField> AudioFields { get; init; }

    public required IReadOnlyList<MediaDetailField> AdvancedFields { get; init; }
}