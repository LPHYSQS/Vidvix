using System.Threading;
using System.Threading.Tasks;
using Vidvix.Core.Models;

namespace Vidvix.Core.Interfaces;

public interface IFFmpegRuntimeService
{
    Task<FFmpegRuntimeResolution> EnsureAvailableAsync(CancellationToken cancellationToken = default);
}
