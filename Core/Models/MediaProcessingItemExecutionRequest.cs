// 功能：媒体处理单文件执行请求（统一描述一次 FFmpeg 执行所需输入）
// 模块：视频转换模块 / 音频转换模块
// 说明：可复用，供 Service 层执行单个转换/提取任务。
using System;

namespace Vidvix.Core.Models;

public sealed class MediaProcessingItemExecutionRequest
{
    public MediaProcessingItemExecutionRequest(
        string runtimeExecutablePath,
        string inputPath,
        string outputPath,
        MediaProcessingContext executionContext)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runtimeExecutablePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(inputPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

        RuntimeExecutablePath = runtimeExecutablePath;
        InputPath = inputPath;
        OutputPath = outputPath;
        ExecutionContext = executionContext;
    }

    public string RuntimeExecutablePath { get; }

    public string InputPath { get; }

    public string OutputPath { get; }

    public MediaProcessingContext ExecutionContext { get; }

    public TimeSpan? InputDuration { get; init; }

    public IProgress<FFmpegProgressUpdate>? Progress { get; init; }

    public Action? OnCpuFallback { get; init; }
}
