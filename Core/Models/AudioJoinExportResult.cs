namespace Vidvix.Core.Models;

public sealed class AudioJoinExportResult
{
    public AudioJoinExportResult(AudioJoinExportRequest request, FFmpegExecutionResult executionResult)
    {
        Request = request;
        ExecutionResult = executionResult;
    }

    public AudioJoinExportRequest Request { get; }

    public FFmpegExecutionResult ExecutionResult { get; }
}
