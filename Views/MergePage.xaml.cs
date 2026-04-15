using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Vidvix.Core.Models;
using Vidvix.ViewModels;

namespace Vidvix.Views;

public sealed partial class MergePage : Page
{
    public static readonly DependencyProperty ViewModelProperty =
        DependencyProperty.Register(
            nameof(ViewModel),
            typeof(MergeViewModel),
            typeof(MergePage),
            new PropertyMetadata(new MergeViewModel()));

    public MergePage()
    {
        InitializeComponent();
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

    private void OnExportButtonClick(object sender, RoutedEventArgs e)
    {
        ViewModel.ExportCommand.Execute(null);

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "导出功能预览",
            Content = ViewModel.BuildExportPreviewMessage(),
            CloseButtonText = "知道了",
            DefaultButton = ContentDialogButton.Close
        };

        _ = dialog.ShowAsync();
    }
}
