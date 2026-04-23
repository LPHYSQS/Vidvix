using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Vidvix.ViewModels;

namespace Vidvix.Views;

public sealed partial class AiPage : Page
{
    private const double CompactLayoutThreshold = 860;
    private bool _isCompactLayout;

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

    private void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        UpdateLayoutState(ActualWidth);
        SetWorkspaceMode(InterpolationModeButton?.IsChecked == true);
    }

    private void OnPageSizeChanged(object sender, SizeChangedEventArgs e) =>
        UpdateLayoutState(e.NewSize.Width);

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

    private void ApplyWideLayout()
    {
        MaterialsColumnDefinition.Width = new GridLength(260);
        WorkspaceColumnDefinition.Width = new GridLength(1, GridUnitType.Star);
        OutputColumnDefinition.Width = new GridLength(320);
        PrimaryRowDefinition.Height = GridLength.Auto;
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
        MaterialsColumnDefinition.Width = new GridLength(1, GridUnitType.Star);
        WorkspaceColumnDefinition.Width = new GridLength(0);
        OutputColumnDefinition.Width = new GridLength(0);
        PrimaryRowDefinition.Height = GridLength.Auto;
        SecondaryRowDefinition.Height = GridLength.Auto;
        TertiaryRowDefinition.Height = GridLength.Auto;

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

    private void OnInterpolationModeChecked(object sender, RoutedEventArgs e) =>
        SetWorkspaceMode(isInterpolationSelected: true);

    private void OnEnhancementModeChecked(object sender, RoutedEventArgs e) =>
        SetWorkspaceMode(isInterpolationSelected: false);

    private void SetWorkspaceMode(bool isInterpolationSelected)
    {
        if (InterpolationWorkspacePanel is null || EnhancementWorkspacePanel is null)
        {
            return;
        }

        InterpolationWorkspacePanel.Visibility = isInterpolationSelected ? Visibility.Visible : Visibility.Collapsed;
        EnhancementWorkspacePanel.Visibility = isInterpolationSelected ? Visibility.Collapsed : Visibility.Visible;
    }
}
