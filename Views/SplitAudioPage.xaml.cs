using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Vidvix.ViewModels;

namespace Vidvix.Views;

public sealed partial class SplitAudioPage : Page
{
    public SplitAudioPage()
    {
        InitializeComponent();
    }

    public SplitAudioWorkspaceViewModel ViewModel
    {
        get => (SplitAudioWorkspaceViewModel)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    public static readonly DependencyProperty ViewModelProperty = DependencyProperty.Register(
        nameof(ViewModel),
        typeof(SplitAudioWorkspaceViewModel),
        typeof(SplitAudioPage),
        new PropertyMetadata(null));
}
