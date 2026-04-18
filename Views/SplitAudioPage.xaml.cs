using Microsoft.UI.Xaml.Controls;

namespace Vidvix.Views;

public sealed partial class SplitAudioPage : Page
{
    public SplitAudioPage()
    {
        InitializeComponent();
    }

    public string VocalFileNameTemplate { get; set; } = "{原文件名}_vocal";

    public string InstrumentalFileNameTemplate { get; set; } = "{原文件名}_instrumental";
}
