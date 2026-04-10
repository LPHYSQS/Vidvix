using System;
using System.Threading;
using System.Threading.Tasks;

namespace Vidvix.Core.Interfaces;

public interface IVideoThumbnailService
{
    Task<Uri?> GetThumbnailUriAsync(string inputPath, CancellationToken cancellationToken = default);
}
