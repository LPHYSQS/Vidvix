namespace Vidvix.Core.Models;

public sealed class AudioJoinExportResult
{
    public AudioJoinExportResult(
        AudioJoinExportRequest request,
        FFmpegExecutionResult executionResult,
        string? transcodingMessage = null,
        bool usedFastPath = false)
    {
        Request = request;
        ExecutionResult = executionResult;
        TranscodingMessage = transcodingMessage;
        UsedFastPath = usedFastPath;
    }

    public AudioJoinExportRequest Request { get; }

    public FFmpegExecutionResult ExecutionResult { get; }

    public string? TranscodingMessage { get; }

    public bool UsedFastPath { get; }
}
