using System;
using System.Threading;
using System.Threading.Tasks;
using Vidvix.Core.Models;

namespace Vidvix.Core.Interfaces;

public interface IAudioJoinWorkflowService
{
    Task<FFmpegRuntimeResolution> EnsureRuntimeReadyAsync(CancellationToken cancellationToken = default);

    Task<AudioJoinExportResult> ExportAsync(
        AudioJoinExportRequest request,
        IProgress<FFmpegProgressUpdate>? progress = null,
        CancellationToken cancellationToken = default);
}
