// 功能：媒体处理单文件执行结果（汇总 FFmpeg 执行结果与回退信息）
// 模块：视频转换模块 / 音频转换模块
// 说明：可复用，供 ViewModel 更新进度和队列状态。
using System;

namespace Vidvix.Core.Models;

public sealed class MediaProcessingItemExecutionResult
{
    public MediaProcessingItemExecutionResult(
        FFmpegExecutionResult executionResult,
        bool usedCpuFallback,
        TimeSpan totalDuration)
    {
        ExecutionResult = executionResult ?? throw new ArgumentNullException(nameof(executionResult));
        UsedCpuFallback = usedCpuFallback;
        TotalDuration = totalDuration;
    }

    public FFmpegExecutionResult ExecutionResult { get; }

    public bool UsedCpuFallback { get; }

    public TimeSpan TotalDuration { get; }
}
