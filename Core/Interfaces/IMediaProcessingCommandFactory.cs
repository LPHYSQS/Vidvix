using Vidvix.Core.Models;

namespace Vidvix.Core.Interfaces;

/// <summary>
/// 负责把“处理意图”转换为具体 FFmpeg 命令，避免 ViewModel 直接耦合命令细节。
/// </summary>
public interface IMediaProcessingCommandFactory
{
    FFmpegCommand Create(MediaProcessingCommandRequest request);

    bool SupportsHardwareVideoEncoding(OutputFormatOption outputFormat);
}
