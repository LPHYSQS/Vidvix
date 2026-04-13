// 功能：媒体处理上下文解析结果（包含最终执行上下文与提示消息）
// 模块：视频转换模块 / 音频转换模块
// 说明：可复用，供 Service 层把 GPU/转码策略解析结果返回给 ViewModel。
using System;
using System.Collections.Generic;

namespace Vidvix.Core.Models;

public sealed class MediaProcessingContextResolutionResult
{
    public MediaProcessingContextResolutionResult(
        MediaProcessingContext context,
        IReadOnlyList<MediaProcessingLogMessage> messages)
    {
        Context = context;
        Messages = messages ?? Array.Empty<MediaProcessingLogMessage>();
    }

    public MediaProcessingContext Context { get; }

    public IReadOnlyList<MediaProcessingLogMessage> Messages { get; }
}
