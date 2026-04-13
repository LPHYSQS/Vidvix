// 功能：媒体处理预检阻塞项模型（描述单个输入文件为何不能继续处理）
// 模块：视频转换模块 / 音频转换模块
// 说明：可复用，供 ViewModel 根据输入路径精确回写队列状态。
using System;

namespace Vidvix.Core.Models;

public sealed class MediaProcessingPreflightIssue
{
    public MediaProcessingPreflightIssue(string inputPath, string failureMessage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(failureMessage);

        InputPath = inputPath;
        FailureMessage = failureMessage;
    }

    public string InputPath { get; }

    public string FailureMessage { get; }
}
