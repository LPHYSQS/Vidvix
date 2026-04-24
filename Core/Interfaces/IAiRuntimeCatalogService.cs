using System.Threading;
using System.Threading.Tasks;
using Vidvix.Core.Models;

namespace Vidvix.Core.Interfaces;

public interface IAiRuntimeCatalogService
{
    Task<AiRuntimeCatalog> GetCatalogAsync(CancellationToken cancellationToken = default);

    Task<AiRuntimeCatalog> EnsureExecutionSupportAsync(
        AiRuntimeKind runtimeKind,
        CancellationToken cancellationToken = default);
}
