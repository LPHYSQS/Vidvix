using System.Threading;
using System.Threading.Tasks;
using Vidvix.Core.Models;

namespace Vidvix.Core.Interfaces;

public interface IAudioSeparationWorkflowService
{
    Task<AudioSeparationResult> SeparateAsync(
        AudioSeparationRequest request,
        CancellationToken cancellationToken = default);
}
