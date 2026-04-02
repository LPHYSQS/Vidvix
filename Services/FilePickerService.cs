using System;
using System.Threading;
using System.Threading.Tasks;
using Vidvix.Core.Interfaces;
using Vidvix.Core.Models;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace Vidvix.Services;

public sealed class FilePickerService : IFilePickerService
{
    private readonly IWindowContext _windowContext;

    public FilePickerService(IWindowContext windowContext)
    {
        _windowContext = windowContext;
    }

    public async Task<string?> PickSingleFileAsync(
        FilePickerRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        cancellationToken.ThrowIfCancellationRequested();

        var picker = new FileOpenPicker
        {
            CommitButtonText = request.CommitButtonText,
            SuggestedStartLocation = PickerLocationId.VideosLibrary,
            ViewMode = PickerViewMode.List
        };

        foreach (var fileType in request.AllowedFileTypes)
        {
            picker.FileTypeFilter.Add(fileType);
        }

        InitializeWithWindow.Initialize(picker, _windowContext.Handle);

        StorageFile? file = await picker.PickSingleFileAsync();
        return file?.Path;
    }
}
