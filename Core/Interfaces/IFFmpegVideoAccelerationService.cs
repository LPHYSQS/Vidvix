using System.Threading;
using System.Threading.Tasks;
using Vidvix.Core.Models;

namespace Vidvix.Core.Interfaces;

public interface IFFmpegVideoAccelerationService
{
    Task<VideoAccelerationProbeResult> ProbeBestEncoderAsync(
        string ffmpegExecutablePath,
        CancellationToken cancellationToken = default);
}
