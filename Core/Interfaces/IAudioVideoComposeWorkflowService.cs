using System;
using System.Threading;
using System.Threading.Tasks;
using Vidvix.Core.Models;

namespace Vidvix.Core.Interfaces;

public interface IAudioVideoComposeWorkflowService
{
    Task<FFmpegRuntimeResolution> EnsureRuntimeReadyAsync(CancellationToken cancellationToken = default);

    Task<AudioVideoComposeExportResult> ExportAsync(
        AudioVideoComposeExportRequest request,
        IProgress<FFmpegProgressUpdate>? progress = null,
        CancellationToken cancellationToken = default);
}
