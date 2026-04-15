// 功能：视频拼接工作流服务接口（封装 FFmpeg 运行时准备与多段视频拼接执行）
// 模块：合并模块
// 说明：供合并 ViewModel 通过统一服务访问底层拼接能力，而不直接耦合 FFmpeg 命令细节。
using System;
using System.Threading;
using System.Threading.Tasks;
using Vidvix.Core.Models;

namespace Vidvix.Core.Interfaces;

public interface IVideoJoinWorkflowService
{
    Task<FFmpegRuntimeResolution> EnsureRuntimeReadyAsync(CancellationToken cancellationToken = default);

    Task<VideoJoinExportResult> ExportAsync(
        VideoJoinExportRequest request,
        IProgress<FFmpegProgressUpdate>? progress = null,
        CancellationToken cancellationToken = default);
}
