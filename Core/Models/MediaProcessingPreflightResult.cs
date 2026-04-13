// 功能：媒体处理预检结果（汇总允许继续的警告消息与阻塞问题）
// 模块：视频转换模块 / 音频转换模块
// 说明：可复用，供 ViewModel 在不直接依赖探测细节的情况下更新队列状态。
using System;
using System.Collections.Generic;

namespace Vidvix.Core.Models;

public sealed class MediaProcessingPreflightResult
{
    public MediaProcessingPreflightResult(
        IReadOnlyList<MediaProcessingLogMessage>? messages = null,
        IReadOnlyList<MediaProcessingPreflightIssue>? blockingIssues = null)
    {
        Messages = messages ?? Array.Empty<MediaProcessingLogMessage>();
        BlockingIssues = blockingIssues ?? Array.Empty<MediaProcessingPreflightIssue>();
    }

    public IReadOnlyList<MediaProcessingLogMessage> Messages { get; }

    public IReadOnlyList<MediaProcessingPreflightIssue> BlockingIssues { get; }
}
