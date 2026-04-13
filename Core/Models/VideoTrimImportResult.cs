// 功能：视频裁剪导入结果模型（封装导入校验、媒体解析与失败详情）
// 模块：裁剪模块
// 说明：可复用，供裁剪 ViewModel 在不依赖探测细节的情况下更新状态。
using System;

namespace Vidvix.Core.Models;

public sealed class VideoTrimImportResult
{
    private VideoTrimImportResult()
    {
    }

    public VideoTrimImportOutcome Outcome { get; private init; }

    public string Message { get; private init; } = string.Empty;

    public string? DiagnosticDetails { get; private init; }

    public string? InputPath { get; private init; }

    public string? InputFileName { get; private init; }

    public MediaDetailsSnapshot? Snapshot { get; private init; }

    public TimeSpan? MediaDuration { get; private init; }

    public bool IsSuccess => Outcome == VideoTrimImportOutcome.Success;

    public static VideoTrimImportResult Success(
        string inputPath,
        string inputFileName,
        MediaDetailsSnapshot snapshot,
        TimeSpan mediaDuration,
        string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(inputFileName);
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        return new VideoTrimImportResult
        {
            Outcome = VideoTrimImportOutcome.Success,
            Message = message,
            InputPath = inputPath,
            InputFileName = inputFileName,
            Snapshot = snapshot,
            MediaDuration = mediaDuration
        };
    }

    public static VideoTrimImportResult Rejected(string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        return new VideoTrimImportResult
        {
            Outcome = VideoTrimImportOutcome.Rejected,
            Message = message
        };
    }

    public static VideoTrimImportResult Failed(string message, string? diagnosticDetails = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        return new VideoTrimImportResult
        {
            Outcome = VideoTrimImportOutcome.Failed,
            Message = message,
            DiagnosticDetails = diagnosticDetails
        };
    }
}
