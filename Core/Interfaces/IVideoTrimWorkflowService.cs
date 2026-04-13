// 功能：视频裁剪工作流服务接口（封装导入校验、媒体解析与导出执行）
// 模块：裁剪模块
// 说明：可复用，供裁剪 ViewModel 通过统一服务访问业务逻辑而不直接依赖底层 FFmpeg 服务。
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Vidvix.Core.Models;

namespace Vidvix.Core.Interfaces;

public interface IVideoTrimWorkflowService
{
    Task<VideoTrimImportResult> ImportAsync(
        IEnumerable<string> inputPaths,
        CancellationToken cancellationToken = default);

    Task<VideoTrimExportResult> ExportAsync(
        VideoTrimExportRequest request,
        UserPreferences preferences,
        IProgress<FFmpegProgressUpdate>? progress = null,
        CancellationToken cancellationToken = default);
}
