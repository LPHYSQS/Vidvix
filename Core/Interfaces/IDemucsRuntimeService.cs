using System.Threading;
using System.Threading.Tasks;
using Vidvix.Core.Models;

namespace Vidvix.Core.Interfaces;

public interface IDemucsRuntimeService
{
    Task<DemucsRuntimeResolution> EnsureAvailableAsync(
        CancellationToken cancellationToken = default,
        DemucsRuntimeVariant runtimeVariant = DemucsRuntimeVariant.Cpu);
}
