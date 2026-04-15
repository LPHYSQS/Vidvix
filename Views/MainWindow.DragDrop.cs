using System;
using System.Linq;
using Microsoft.UI.Xaml;
using Windows.ApplicationModel.DataTransfer;

namespace Vidvix.Views;

public sealed partial class MainWindow
{
    private void OnRootDragOver(object sender, DragEventArgs e)
    {
        if (!ViewModel.CanModifyInputs)
        {
            e.AcceptedOperation = DataPackageOperation.None;
            return;
        }

        if (!e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            e.AcceptedOperation = DataPackageOperation.None;
            return;
        }

        if (ViewModel.IsMergeWorkspaceSelected)
        {
            e.AcceptedOperation = DataPackageOperation.None;
            return;
        }

        e.AcceptedOperation = DataPackageOperation.Copy;
        e.DragUIOverride.Caption = ViewModel.IsTrimWorkspaceSelected
            ? ViewModel.TrimWorkspace.DragDropCaptionText
            : ViewModel.DragDropCaptionText;
        e.DragUIOverride.IsCaptionVisible = true;
        e.DragUIOverride.IsContentVisible = true;
        e.Handled = true;
    }

    private async void OnRootDrop(object sender, DragEventArgs e)
    {
        if (!ViewModel.CanModifyInputs || !e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            return;
        }

        var storageItems = await e.DataView.GetStorageItemsAsync();
        var paths = storageItems
            .Where(item => !string.IsNullOrWhiteSpace(item.Path))
            .Select(item => item.Path)
            .ToArray();

        if (paths.Length == 0)
        {
            return;
        }

        if (ViewModel.IsMergeWorkspaceSelected)
        {
            return;
        }

        if (ViewModel.IsTrimWorkspaceSelected)
        {
            await ViewModel.TrimWorkspace.ImportPathsAsync(paths);
            return;
        }

        await ViewModel.ImportPathsAsync(paths);
    }
}
