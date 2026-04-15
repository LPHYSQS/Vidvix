using System;

namespace Vidvix.Core.Models;

public sealed class VideoJoinExportResult
{
    public VideoJoinExportResult(VideoJoinExportRequest request, FFmpegExecutionResult executionResult)
    {
        Request = request ?? throw new ArgumentNullException(nameof(request));
        ExecutionResult = executionResult ?? throw new ArgumentNullException(nameof(executionResult));
    }

    public VideoJoinExportRequest Request { get; }

    public FFmpegExecutionResult ExecutionResult { get; }
}
