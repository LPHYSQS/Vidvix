using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Vidvix.ViewModels;

namespace Vidvix.Views.Controls;

public sealed partial class TerminalCommandComposer : UserControl
{
    public TerminalCommandComposer()
    {
        InitializeComponent();
    }

    public TerminalWorkspaceViewModel ViewModel
    {
        get => (TerminalWorkspaceViewModel)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    public static readonly DependencyProperty ViewModelProperty = DependencyProperty.Register(
        nameof(ViewModel),
        typeof(TerminalWorkspaceViewModel),
        typeof(TerminalCommandComposer),
        new PropertyMetadata(new TerminalWorkspaceViewModel()));

    private void OnCommandTextBoxKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != Windows.System.VirtualKey.Enter)
        {
            return;
        }

        if (ViewModel.ExecuteCommand.CanExecute(null))
        {
            ViewModel.ExecuteCommand.Execute(null);
            e.Handled = true;
        }
    }
}
