// 功能：视频裁剪导出结果模型（封装最终执行请求与 FFmpeg 返回值）
// 模块：裁剪模块
// 说明：可复用，供裁剪 ViewModel 在不直接依赖底层执行细节的情况下更新 UI。
using System;

namespace Vidvix.Core.Models;

public sealed class VideoTrimExportResult
{
    public VideoTrimExportResult(VideoTrimExportRequest request, FFmpegExecutionResult executionResult)
    {
        Request = request;
        ExecutionResult = executionResult ?? throw new ArgumentNullException(nameof(executionResult));
    }

    public VideoTrimExportRequest Request { get; }

    public FFmpegExecutionResult ExecutionResult { get; }
}
