using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Vidvix.Core.Models;

namespace Vidvix.Core.Interfaces;

public interface IDemucsExecutionPlanner
{
    Task<IReadOnlyList<DemucsExecutionPlan>> ResolveExecutionPlansAsync(
        DemucsAccelerationMode accelerationMode,
        CancellationToken cancellationToken = default);
}
