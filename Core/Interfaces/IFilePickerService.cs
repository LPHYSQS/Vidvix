using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Vidvix.Core.Models;

namespace Vidvix.Core.Interfaces;

public interface IFilePickerService
{
    Task<string?> PickSingleFileAsync(
        FilePickerRequest request,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> PickFilesAsync(
        FilePickerRequest request,
        CancellationToken cancellationToken = default);

    Task<string?> PickFolderAsync(
        string commitButtonText,
        CancellationToken cancellationToken = default);
}
