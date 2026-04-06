using System;
using System.Collections.Generic;
using System.Linq;
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

    public async Task<IReadOnlyList<string>> PickFilesAsync(
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

        IReadOnlyList<StorageFile> files = await picker.PickMultipleFilesAsync();
        return files
            .Select(file => file.Path)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .ToArray();
    }

    public async Task<string?> PickFolderAsync(
        string commitButtonText,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commitButtonText);

        cancellationToken.ThrowIfCancellationRequested();

        var picker = new FolderPicker
        {
            CommitButtonText = commitButtonText,
            SuggestedStartLocation = PickerLocationId.VideosLibrary,
            ViewMode = PickerViewMode.List
        };

        picker.FileTypeFilter.Add("*");

        InitializeWithWindow.Initialize(picker, _windowContext.Handle);

        StorageFolder? folder = await picker.PickSingleFolderAsync();
        return folder?.Path;
    }
}
