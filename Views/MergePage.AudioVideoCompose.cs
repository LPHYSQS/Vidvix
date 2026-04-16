using Microsoft.UI.Xaml;
using Vidvix.Core.Models;

namespace Vidvix.Views;

public sealed partial class MergePage
{
    private void OnSetAudioVideoComposeVideoPresetClick(object sender, RoutedEventArgs e)
    {
        if (TryResolveTrackItem(sender, out var trackItem))
        {
            ViewModel.SetAudioVideoComposeVideoPreset(trackItem);
        }
    }

    private void OnSetAudioVideoComposeAudioPresetClick(object sender, RoutedEventArgs e)
    {
        if (TryResolveTrackItem(sender, out var trackItem))
        {
            ViewModel.SetAudioVideoComposeAudioPreset(trackItem);
        }
    }
}
