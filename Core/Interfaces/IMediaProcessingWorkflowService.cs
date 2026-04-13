// 功能：媒体批处理工作流服务接口（封装运行时准备、预检与单文件执行流程）
// 模块：视频转换模块 / 音频转换模块
// 说明：可复用，供 ViewModel 通过统一服务编排处理任务而不直接依赖 FFmpeg 细节。
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Vidvix.Core.Models;

namespace Vidvix.Core.Interfaces;

public interface IMediaProcessingWorkflowService
{
    Task<FFmpegRuntimeResolution> EnsureRuntimeReadyAsync(CancellationToken cancellationToken = default);

    Task<MediaProcessingContextResolutionResult> ResolveExecutionContextAsync(
        string runtimeExecutablePath,
        MediaProcessingContext executionContext,
        CancellationToken cancellationToken = default);

    Task<MediaProcessingPreflightResult> ValidatePreconditionsAsync(
        MediaProcessingContext executionContext,
        IReadOnlyList<string> inputPaths,
        CancellationToken cancellationToken = default);

    Task<TimeSpan?> GetMediaDurationAsync(string inputPath, CancellationToken cancellationToken = default);

    Task<MediaProcessingItemExecutionResult> ExecuteAsync(
        MediaProcessingItemExecutionRequest request,
        CancellationToken cancellationToken = default);
}
