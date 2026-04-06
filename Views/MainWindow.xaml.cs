using System;
using System.Linq;
using System.Numerics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Vidvix.ViewModels;
using Windows.ApplicationModel.DataTransfer;

namespace Vidvix.Views;

public sealed partial class MainWindow : Window
{
    private bool _runtimePulseStarted;

    public MainWindow(MainViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        Closed += OnClosed;
    }

    public MainViewModel ViewModel { get; }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        Closed -= OnClosed;
        ViewModel.Dispose();
    }

    private void OnRootLayoutLoaded(object sender, RoutedEventArgs e)
    {
        if (_runtimePulseStarted)
        {
            return;
        }

        _runtimePulseStarted = true;

        var haloVisual = ElementCompositionPreview.GetElementVisual(RuntimePulseHalo);
        haloVisual.CenterPoint = new Vector3(12f, 12f, 0f);

        var compositor = haloVisual.Compositor;

        var opacityAnimation = compositor.CreateScalarKeyFrameAnimation();
        opacityAnimation.InsertKeyFrame(0f, 0.18f);
        opacityAnimation.InsertKeyFrame(0.5f, 0.72f);
        opacityAnimation.InsertKeyFrame(1f, 0.18f);
        opacityAnimation.Duration = TimeSpan.FromSeconds(2.2);
        opacityAnimation.IterationBehavior = Microsoft.UI.Composition.AnimationIterationBehavior.Forever;

        var scaleAnimation = compositor.CreateVector3KeyFrameAnimation();
        scaleAnimation.InsertKeyFrame(0f, new Vector3(0.82f, 0.82f, 1f));
        scaleAnimation.InsertKeyFrame(0.5f, new Vector3(1.18f, 1.18f, 1f));
        scaleAnimation.InsertKeyFrame(1f, new Vector3(0.82f, 0.82f, 1f));
        scaleAnimation.Duration = TimeSpan.FromSeconds(2.2);
        scaleAnimation.IterationBehavior = Microsoft.UI.Composition.AnimationIterationBehavior.Forever;

        haloVisual.StartAnimation("Opacity", opacityAnimation);
        haloVisual.StartAnimation("Scale", scaleAnimation);
    }

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

        e.AcceptedOperation = DataPackageOperation.Copy;
        e.DragUIOverride.Caption = "导入视频文件或文件夹";
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

        await ViewModel.ImportPathsAsync(paths);
    }
}
