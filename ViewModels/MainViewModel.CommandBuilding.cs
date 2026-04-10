using System;
using Vidvix.Core.Models;

namespace Vidvix.ViewModels;

public sealed partial class MainViewModel
{
    // 这里仅保留 ViewModel 到命令工厂的适配，真正的 FFmpeg 拼装策略已经下沉到独立服务。

    private FFmpegCommand BuildCommand(string inputPath, string outputPath, MediaProcessingContext executionContext)
    {
        if (string.IsNullOrWhiteSpace(_runtimeExecutablePath))
        {
            throw new InvalidOperationException("运行环境尚未准备完成。");
        }

        return _mediaProcessingCommandFactory.Create(
            new MediaProcessingCommandRequest(
                _runtimeExecutablePath,
                inputPath,
                outputPath,
                executionContext));
    }

    private bool CanUseHardwareVideoEncoding(OutputFormatOption outputFormat) =>
        _mediaProcessingCommandFactory.SupportsHardwareVideoEncoding(outputFormat);
}
