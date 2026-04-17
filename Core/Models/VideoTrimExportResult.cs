// 功能：视频裁剪导出结果模型（封装最终执行请求与 FFmpeg 返回值）
// 模块：裁剪模块
// 说明：可复用，供裁剪 ViewModel 在不直接依赖底层执行细节的情况下更新 UI。
using System;

namespace Vidvix.Core.Models;

public sealed class VideoTrimExportResult
{
    public VideoTrimExportResult(
        VideoTrimExportRequest request,
        FFmpegExecutionResult executionResult,
        string? transcodingMessage = null,
        bool usedFastPath = false,
        bool usedCpuFallback = false)
    {
        Request = request;
        ExecutionResult = executionResult ?? throw new ArgumentNullException(nameof(executionResult));
        TranscodingMessage = transcodingMessage;
        UsedFastPath = usedFastPath;
        UsedCpuFallback = usedCpuFallback;
    }

    public VideoTrimExportRequest Request { get; }

    public FFmpegExecutionResult ExecutionResult { get; }

    public string? TranscodingMessage { get; }

    public bool UsedFastPath { get; }

    public bool UsedCpuFallback { get; }
}
