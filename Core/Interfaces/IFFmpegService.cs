using System.Threading;
using System.Threading.Tasks;
using Vidvix.Core.Models;

namespace Vidvix.Core.Interfaces;

public interface IFFmpegService
{
    Task<FFmpegExecutionResult> ExecuteAsync(
        FFmpegCommand command,
        FFmpegExecutionOptions? executionOptions = null,
        CancellationToken cancellationToken = default);
}

