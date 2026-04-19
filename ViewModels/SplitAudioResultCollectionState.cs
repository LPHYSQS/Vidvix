using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Vidvix.Core.Models;

namespace Vidvix.ViewModels;

internal sealed class SplitAudioResultCollectionState
{
    public ObservableCollection<SplitAudioResultItemViewModel> Items { get; } = new();

    public bool HasResults => Items.Count > 0;

    public Visibility ResultsVisibility => HasResults ? Visibility.Visible : Visibility.Collapsed;

    public Visibility EmptyResultsVisibility => HasResults ? Visibility.Collapsed : Visibility.Visible;

    public void Prepend(AudioSeparationResult result) =>
        Items.Insert(0, new SplitAudioResultItemViewModel(result));

    public void Clear() => Items.Clear();
}
