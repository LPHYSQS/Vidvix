using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Vidvix.ViewModels;

namespace Vidvix.Views.Controls;

public sealed partial class TerminalOutputPanel : UserControl
{
    private TerminalWorkspaceViewModel? _observedViewModel;

    public TerminalOutputPanel()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public TerminalWorkspaceViewModel ViewModel
    {
        get => (TerminalWorkspaceViewModel)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    public static readonly DependencyProperty ViewModelProperty = DependencyProperty.Register(
        nameof(ViewModel),
        typeof(TerminalWorkspaceViewModel),
        typeof(TerminalOutputPanel),
        new PropertyMetadata(new TerminalWorkspaceViewModel(), OnViewModelChanged));

    private static void OnViewModelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TerminalOutputPanel panel)
        {
            return;
        }

        panel.DetachFromViewModel(e.OldValue as TerminalWorkspaceViewModel);
        panel.AttachToViewModel(e.NewValue as TerminalWorkspaceViewModel);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        AttachToViewModel(ViewModel);
        ScrollToEnd();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e) =>
        DetachFromViewModel(_observedViewModel);

    private void AttachToViewModel(TerminalWorkspaceViewModel? viewModel)
    {
        if (viewModel is null || ReferenceEquals(_observedViewModel, viewModel))
        {
            return;
        }

        DetachFromViewModel(_observedViewModel);
        _observedViewModel = viewModel;
        _observedViewModel.ScrollToEndRequested += OnScrollToEndRequested;
    }

    private void DetachFromViewModel(TerminalWorkspaceViewModel? viewModel)
    {
        if (viewModel is null)
        {
            return;
        }

        viewModel.ScrollToEndRequested -= OnScrollToEndRequested;

        if (ReferenceEquals(_observedViewModel, viewModel))
        {
            _observedViewModel = null;
        }
    }

    private void OnScrollToEndRequested(object? sender, EventArgs e) =>
        ScrollToEnd();

    private void ScrollToEnd()
    {
        DispatcherQueue?.TryEnqueue(() =>
        {
            OutputScrollViewer?.ChangeView(null, OutputScrollViewer.ScrollableHeight, null, disableAnimation: true);
        });
    }
}
