using Vidvix.Core.Models;

namespace Vidvix.Core.Interfaces;

public interface IVideoTrimCommandFactory
{
    FFmpegCommand Create(VideoTrimExportRequest request, string runtimeExecutablePath);
}
