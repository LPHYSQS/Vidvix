using Microsoft.UI.Xaml;
using Vidvix.ViewModels;

namespace Vidvix.Views;

public sealed partial class MainWindow : Window
{
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
}
