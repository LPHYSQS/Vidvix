using System;

namespace Vidvix.Core.Models;

public sealed class AudioVideoComposeExportResult
{
    public AudioVideoComposeExportResult(AudioVideoComposeExportRequest request, FFmpegExecutionResult executionResult)
    {
        Request = request ?? throw new ArgumentNullException(nameof(request));
        ExecutionResult = executionResult ?? throw new ArgumentNullException(nameof(executionResult));
    }

    public AudioVideoComposeExportRequest Request { get; }

    public FFmpegExecutionResult ExecutionResult { get; }
}
