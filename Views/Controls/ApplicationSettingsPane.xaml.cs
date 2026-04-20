using System.Windows.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Vidvix.ViewModels;

namespace Vidvix.Views.Controls;

public sealed partial class ApplicationSettingsPane : UserControl
{
    private MainViewModel? _registeredViewModel;

    public ApplicationSettingsPane()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public MainViewModel? ViewModel
    {
        get => (MainViewModel?)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    public static readonly DependencyProperty ViewModelProperty = DependencyProperty.Register(
        nameof(ViewModel),
        typeof(MainViewModel),
        typeof(ApplicationSettingsPane),
        new PropertyMetadata(null, OnViewModelChanged));

    public ICommand? CloseCommand
    {
        get => (ICommand?)GetValue(CloseCommandProperty);
        set => SetValue(CloseCommandProperty, value);
    }

    public static readonly DependencyProperty CloseCommandProperty = DependencyProperty.Register(
        nameof(CloseCommand),
        typeof(ICommand),
        typeof(ApplicationSettingsPane),
        new PropertyMetadata(null));

    private static void OnViewModelChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        if (dependencyObject is not ApplicationSettingsPane pane)
        {
            return;
        }

        pane.UnregisterLocalizationRefresh(args.OldValue as MainViewModel);
        pane.RegisterLocalizationRefresh(args.NewValue as MainViewModel);
        pane.Bindings.Update();
    }

    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        RegisterLocalizationRefresh(ViewModel);
        Bindings.Update();
    }

    private void OnUnloaded(object sender, RoutedEventArgs args) =>
        UnregisterLocalizationRefresh(_registeredViewModel);

    private void RegisterLocalizationRefresh(MainViewModel? viewModel)
    {
        if (viewModel is null || ReferenceEquals(_registeredViewModel, viewModel))
        {
            return;
        }

        _registeredViewModel = viewModel;
        _registeredViewModel.LocalizationRefreshRequested += OnLocalizationRefreshRequested;
    }

    private void UnregisterLocalizationRefresh(MainViewModel? viewModel)
    {
        if (viewModel is null)
        {
            return;
        }

        viewModel.LocalizationRefreshRequested -= OnLocalizationRefreshRequested;

        if (ReferenceEquals(_registeredViewModel, viewModel))
        {
            _registeredViewModel = null;
        }
    }

    private void OnLocalizationRefreshRequested() =>
        Bindings.Update();
}
