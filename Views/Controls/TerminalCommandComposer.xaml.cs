using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
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
}
