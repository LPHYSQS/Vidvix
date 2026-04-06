namespace Vidvix.Core.Models;

public sealed class FFmpegRuntimeResolution
{
    public required string ExecutablePath { get; init; }

    public required string StorageRootPath { get; init; }

    public bool WasDownloaded { get; init; }
}
