using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Vidvix.ViewModels;
using Windows.ApplicationModel.DataTransfer;

namespace Vidvix.Views;

public sealed partial class AiPage : Page
{
    private const double CompactLayoutThreshold = 860;
    private bool _isCompactLayout;
    private bool? _canAcceptCurrentMaterialDrop;

    public AiPage()
    {
        InitializeComponent();
    }

    public AiWorkspaceViewModel ViewModel
    {
        get => (AiWorkspaceViewModel)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    public static readonly DependencyProperty ViewModelProperty = DependencyProperty.Register(
        nameof(ViewModel),
        typeof(AiWorkspaceViewModel),
        typeof(AiPage),
        new PropertyMetadata(new AiWorkspaceViewModel()));

    private async void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        UpdateLayoutHeight(ActualHeight);
        UpdateLayoutState(ActualWidth);
        await ViewModel.InitializeRuntimeAsync();
    }

    private void OnPageSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateLayoutHeight(e.NewSize.Height);
        UpdateLayoutState(e.NewSize.Width);
    }

    private async void OnMaterialsListDragEnter(object sender, DragEventArgs e)
    {
        _canAcceptCurrentMaterialDrop = await ResolveMaterialDropAvailabilityAsync(e);
        ApplyMaterialDropOperation(e, _canAcceptCurrentMaterialDrop == true);
    }

    private void OnMaterialsListDragLeave(object sender, DragEventArgs e)
    {
        _canAcceptCurrentMaterialDrop = null;
        RejectMaterialDrop(e);
    }

    private void OnMaterialsListDragOver(object sender, DragEventArgs e)
    {
        ApplyMaterialDropOperation(e, _canAcceptCurrentMaterialDrop == true);
    }

    private async void OnMaterialsListDrop(object sender, DragEventArgs e)
    {
        if (!ViewModel.CanModifyMaterials || !e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            RejectMaterialDrop(e);
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

            if (!ViewModel.CanImportPaths(paths))
            {
                RejectMaterialDrop(e);
                return;
            }

            await ViewModel.ImportPathsAsync(paths);
            e.AcceptedOperation = DataPackageOperation.Copy;
            e.Handled = true;
        }
        finally
        {
            _canAcceptCurrentMaterialDrop = null;
            deferral.Complete();
        }
    }

    private void UpdateLayoutState(double availableWidth)
    {
        var layoutWidth = availableWidth > 0 ? availableWidth : LayoutRoot.ActualWidth;
        var shouldUseCompactLayout = layoutWidth > 0 && layoutWidth < CompactLayoutThreshold;
        if (_isCompactLayout == shouldUseCompactLayout)
        {
            return;
        }

        _isCompactLayout = shouldUseCompactLayout;

        if (shouldUseCompactLayout)
        {
            ApplyCompactLayout();
            return;
        }

        ApplyWideLayout();
    }

    private void UpdateLayoutHeight(double availableHeight)
    {
        if (availableHeight <= 0 || LayoutRoot is null)
        {
            return;
        }

        LayoutRoot.MinHeight = availableHeight;
    }

    private void ApplyWideLayout()
    {
        LayoutRoot.RowSpacing = 0;
        MaterialsColumnDefinition.Width = new GridLength(260);
        WorkspaceColumnDefinition.Width = new GridLength(1, GridUnitType.Star);
        OutputColumnDefinition.Width = new GridLength(320);
        PrimaryRowDefinition.Height = new GridLength(1, GridUnitType.Star);
        SecondaryRowDefinition.Height = new GridLength(0);
        TertiaryRowDefinition.Height = new GridLength(0);

        Grid.SetRow(MaterialsCard, 0);
        Grid.SetColumn(MaterialsCard, 0);
        Grid.SetColumnSpan(MaterialsCard, 1);

        Grid.SetRow(WorkspaceCard, 0);
        Grid.SetColumn(WorkspaceCard, 1);
        Grid.SetColumnSpan(WorkspaceCard, 1);

        Grid.SetRow(OutputCard, 0);
        Grid.SetColumn(OutputCard, 2);
        Grid.SetColumnSpan(OutputCard, 1);
    }

    private void ApplyCompactLayout()
    {
        LayoutRoot.RowSpacing = 12;
        MaterialsColumnDefinition.Width = new GridLength(1, GridUnitType.Star);
        WorkspaceColumnDefinition.Width = new GridLength(0);
        OutputColumnDefinition.Width = new GridLength(0);
        PrimaryRowDefinition.Height = new GridLength(1, GridUnitType.Star);
        SecondaryRowDefinition.Height = new GridLength(1, GridUnitType.Star);
        TertiaryRowDefinition.Height = new GridLength(1, GridUnitType.Star);

        Grid.SetRow(MaterialsCard, 0);
        Grid.SetColumn(MaterialsCard, 0);
        Grid.SetColumnSpan(MaterialsCard, 1);

        Grid.SetRow(WorkspaceCard, 1);
        Grid.SetColumn(WorkspaceCard, 0);
        Grid.SetColumnSpan(WorkspaceCard, 1);

        Grid.SetRow(OutputCard, 2);
        Grid.SetColumn(OutputCard, 0);
        Grid.SetColumnSpan(OutputCard, 1);
    }

    private async Task<bool> ResolveMaterialDropAvailabilityAsync(DragEventArgs e)
    {
        if (!ViewModel.CanModifyMaterials || !e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            return false;
        }

        var deferral = e.GetDeferral();
        try
        {
            var storageItems = await e.DataView.GetStorageItemsAsync();
            var paths = storageItems
                .Where(item => !string.IsNullOrWhiteSpace(item.Path))
                .Select(item => item.Path)
                .ToArray();

            return ViewModel.CanImportPaths(paths);
        }
        finally
        {
            deferral.Complete();
        }
    }

    private void ApplyMaterialDropOperation(DragEventArgs e, bool canAcceptDrop)
    {
        if (!canAcceptDrop)
        {
            RejectMaterialDrop(e);
            return;
        }

        e.AcceptedOperation = DataPackageOperation.Copy;
        e.DragUIOverride.Caption = ViewModel.MaterialsDragDropCaptionText;
        e.DragUIOverride.IsCaptionVisible = true;
        e.DragUIOverride.IsContentVisible = true;
        e.Handled = true;
    }

    private static void RejectMaterialDrop(DragEventArgs e)
    {
        e.AcceptedOperation = DataPackageOperation.None;
        e.Handled = true;
    }
}
