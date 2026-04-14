using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Vidvix.Core.Models;

namespace Vidvix.Core.Interfaces;

public interface ITrimWorkflowService
{
    Task<VideoTrimImportResult> ImportAsync(
        IEnumerable<string> inputPaths,
        CancellationToken cancellationToken = default);

    Task<VideoTrimExportResult> ExportAsync(
        VideoTrimExportRequest request,
        UserPreferences preferences,
        IProgress<FFmpegProgressUpdate>? progress = null,
        CancellationToken cancellationToken = default);
}
