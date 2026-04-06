using System;

namespace Vidvix.Core.Models;

public sealed class FFmpegPackageManifest
{
    public required Uri ArchiveUri { get; init; }

    public required Uri ChecksumUri { get; init; }

    public required string ArchiveFileName { get; init; }

    public string? ExpectedSha256 { get; init; }

    public string PackageVersionLabel { get; init; } = "latest";
}
