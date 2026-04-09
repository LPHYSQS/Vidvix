using System.Threading;
using System.Threading.Tasks;
using Vidvix.Core.Models;

namespace Vidvix.Core.Interfaces;

public interface IMediaInfoService
{
    bool TryGetCachedDetails(string inputPath, out MediaDetailsSnapshot snapshot);

    Task<MediaDetailsLoadResult> GetMediaDetailsAsync(string inputPath, CancellationToken cancellationToken = default);
}