using System;

namespace Vidvix.Core.Models;

public sealed class VideoJoinExportResult
{
    public VideoJoinExportResult(
        VideoJoinExportRequest request,
        FFmpegExecutionResult executionResult,
        string? transcodingMessage = null,
        bool usedFastPath = false,
        bool usedCpuFallback = false)
    {
        Request = request ?? throw new ArgumentNullException(nameof(request));
        ExecutionResult = executionResult ?? throw new ArgumentNullException(nameof(executionResult));
        TranscodingMessage = transcodingMessage;
        UsedFastPath = usedFastPath;
        UsedCpuFallback = usedCpuFallback;
    }

    public VideoJoinExportRequest Request { get; }

    public FFmpegExecutionResult ExecutionResult { get; }

    public string? TranscodingMessage { get; }

    public bool UsedFastPath { get; }

    public bool UsedCpuFallback { get; }
}
