using System;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Vidvix.Core.Models;
using Vidvix.ViewModels;
using Windows.ApplicationModel.DataTransfer;

namespace Vidvix.Views;

public sealed partial class MergePage : Page
{
    private MergeViewModel? _subscribedViewModel;

    public static readonly DependencyProperty ViewModelProperty =
        DependencyProperty.Register(
            nameof(ViewModel),
            typeof(MergeViewModel),
            typeof(MergePage),
            new PropertyMetadata(new MergeViewModel(), OnViewModelPropertyChanged));

    public MergePage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public MergeViewModel ViewModel
    {
        get => (MergeViewModel)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    private void OnMediaListDragEnter(object sender, DragEventArgs e)
    {
        UpdateMediaListDropOperation(e);
    }

    private void OnMediaListDragOver(object sender, DragEventArgs e)
    {
        UpdateMediaListDropOperation(e);
    }

    private async void OnMediaListDrop(object sender, DragEventArgs e)
    {
        if (!ViewModel.CanModifyWorkspace || !e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            e.AcceptedOperation = DataPackageOperation.None;
            e.Handled = true;
            return;
        }

        var deferral = e.GetDeferral();
        try
        {
            var storageItems = await e.DataView.GetStorageItemsAsync();
            var paths = storageItems
                .Where(item => !string.IsNullOrWhiteSpace(item.Path))
                .Select(item => item.Path)
                .ToArray();

            if (paths.Length == 0)
            {
                e.AcceptedOperation = DataPackageOperation.None;
                e.Handled = true;
                return;
            }

            await ViewModel.ImportPathsAsync(paths);
            e.AcceptedOperation = DataPackageOperation.Copy;
            e.Handled = true;
        }
        finally
        {
            deferral.Complete();
        }
    }

    private void OnMediaItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is MediaItem mediaItem)
        {
            ViewModel.AddMediaToTimeline(mediaItem);
        }
    }

    private void OnRemoveMediaItemClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: MediaItem mediaItem })
        {
            ViewModel.RemoveMediaItem(mediaItem);
        }
    }

    private void OnRemoveVideoTrackItemClick(object sender, RoutedEventArgs e)
    {
        if (TryResolveTrackItem(sender, out var trackItem))
        {
            ViewModel.RemoveVideoTrackItem(trackItem);
        }
    }

    private void OnSetVideoResolutionPresetClick(object sender, RoutedEventArgs e)
    {
        if (TryResolveTrackItem(sender, out var trackItem))
        {
            ViewModel.SetVideoResolutionPreset(trackItem);
        }
    }

    private void OnRemoveAudioTrackItemClick(object sender, RoutedEventArgs e)
    {
        if (TryResolveTrackItem(sender, out var trackItem))
        {
            ViewModel.RemoveAudioTrackItem(trackItem);
        }
    }

    private void OnSetAudioParameterPresetClick(object sender, RoutedEventArgs e)
    {
        if (TryResolveTrackItem(sender, out var trackItem))
        {
            ViewModel.SetAudioParameterPreset(trackItem);
        }
    }

    private static bool TryResolveTrackItem(object sender, out TrackItem trackItem)
    {
        if (sender is FrameworkElement { Tag: TrackItem taggedTrackItem })
        {
            trackItem = taggedTrackItem;
            return true;
        }

        if (sender is FrameworkElement { DataContext: TrackItem dataContextTrackItem })
        {
            trackItem = dataContextTrackItem;
            return true;
        }

        trackItem = null!;
        return false;
    }

    private static void OnViewModelPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is MergePage page)
        {
            page.AttachViewModelNotifications(
                e.OldValue as MergeViewModel,
                e.NewValue as MergeViewModel);
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        AttachViewModelNotifications(_subscribedViewModel, ViewModel);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        AttachViewModelNotifications(_subscribedViewModel, null);
    }

    private void AttachViewModelNotifications(MergeViewModel? oldViewModel, MergeViewModel? newViewModel)
    {
        if (oldViewModel is not null && ReferenceEquals(_subscribedViewModel, oldViewModel))
        {
            oldViewModel.InvalidTrackItemsDetected -= OnInvalidTrackItemsDetected;
            _subscribedViewModel = null;
        }

        if (!IsLoaded || newViewModel is null || ReferenceEquals(_subscribedViewModel, newViewModel))
        {
            return;
        }

        newViewModel.InvalidTrackItemsDetected += OnInvalidTrackItemsDetected;
        _subscribedViewModel = newViewModel;
    }

    private async void OnInvalidTrackItemsDetected(string title, string message)
    {
        if (XamlRoot is null)
        {
            return;
        }

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = title,
            Content = message,
            CloseButtonText = ViewModel.InvalidTrackDialogCloseButtonText,
            DefaultButton = ContentDialogButton.Close
        };

        await dialog.ShowAsync();
    }

    private void UpdateMediaListDropOperation(DragEventArgs e)
    {
        if (!ViewModel.CanModifyWorkspace || !e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            e.AcceptedOperation = DataPackageOperation.None;
            e.Handled = true;
            return;
        }

        e.AcceptedOperation = DataPackageOperation.Copy;
        e.DragUIOverride.Caption = ViewModel.MediaListDragDropCaptionText;
        e.DragUIOverride.IsCaptionVisible = true;
        e.DragUIOverride.IsContentVisible = true;
        e.Handled = true;
    }
}
