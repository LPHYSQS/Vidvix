using Vidvix.Core.Models;

namespace Vidvix.Core.Interfaces;

public interface IAudioTrimCommandFactory
{
    FFmpegCommand Create(VideoTrimExportRequest request, string runtimeExecutablePath);
}
