using System.Threading;
using System.Threading.Tasks;
using Vidvix.Core.Models;

namespace Vidvix.Core.Interfaces;

public interface IFilePickerService
{
    Task<string?> PickSingleFileAsync(
        FilePickerRequest request,
        CancellationToken cancellationToken = default);
}

