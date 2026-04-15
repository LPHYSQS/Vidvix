using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Vidvix.Core.Models;
using Vidvix.ViewModels;

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
            CloseButtonText = "知道了",
            DefaultButton = ContentDialogButton.Close
        };

        await dialog.ShowAsync();
    }
}
