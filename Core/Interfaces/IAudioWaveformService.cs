using System;
using System.Threading;
using System.Threading.Tasks;

namespace Vidvix.Core.Interfaces;

public interface IAudioWaveformService
{
    Task<Uri?> GetWaveformImageUriAsync(string inputPath, CancellationToken cancellationToken = default);
}
