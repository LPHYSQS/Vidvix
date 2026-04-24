using System.Threading;
using System.Threading.Tasks;
using Vidvix.Core.Models;

namespace Vidvix.Core.Interfaces;

public interface IAiInterpolationWorkflowService
{
    Task<AiInterpolationResult> InterpolateAsync(
        AiInterpolationRequest request,
        CancellationToken cancellationToken = default);
}
