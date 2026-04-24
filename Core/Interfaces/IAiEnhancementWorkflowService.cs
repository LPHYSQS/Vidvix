using System.Threading;
using System.Threading.Tasks;
using Vidvix.Core.Models;

namespace Vidvix.Core.Interfaces;

public interface IAiEnhancementWorkflowService
{
    Task<AiEnhancementResult> EnhanceAsync(
        AiEnhancementRequest request,
        CancellationToken cancellationToken = default);
}
