using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Vidvix.ViewModels;

namespace Vidvix.Views;

public sealed partial class AiPage : Page
{
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
}
