using System.Collections.Generic;
using System.Linq;
using Vidvix.Core.Models;

namespace Vidvix.ViewModels;

public sealed partial class MergeViewModel
{
    public void RefreshLocalization()
    {
        var selectedVideoJoinExtension = _selectedVideoJoinOutputFormat?.Extension;
        var selectedAudioJoinExtension = _selectedAudioJoinOutputFormat?.Extension;
        var selectedAudioVideoComposeExtension = _selectedAudioVideoComposeOutputFormat?.Extension;

        _videoJoinOutputFormats = BuildVideoJoinOutputFormats();
        _audioJoinOutputFormats = BuildAudioJoinOutputFormats();
        _modeStates = CreateModeStates(_configuration);

        _selectedVideoJoinOutputFormat = ResolvePreferredVideoJoinOutputFormat(selectedVideoJoinExtension);
        _selectedAudioJoinOutputFormat = ResolvePreferredAudioJoinOutputFormat(selectedAudioJoinExtension);
        _selectedAudioVideoComposeOutputFormat =
            ResolvePreferredAudioVideoComposeOutputFormat(selectedAudioVideoComposeExtension);

        OnPropertyChanged(nameof(VideoJoinOutputFormats));
        OnPropertyChanged(nameof(AudioJoinOutputFormats));
        OnPropertyChanged(nameof(AudioVideoComposeOutputFormats));
        OnPropertyChanged(nameof(SelectedOutputFormat));
        OnPropertyChanged(nameof(SelectedOutputFormatDescription));
        OnPropertyChanged(nameof(SelectedAudioJoinOutputFormat));
        OnPropertyChanged(nameof(SelectedAudioJoinOutputFormatDescription));
        OnPropertyChanged(nameof(SelectedAudioVideoComposeOutputFormat));
        OnPropertyChanged(nameof(SelectedAudioVideoComposeOutputFormatDescription));
        OnPropertyChanged(nameof(AudioVideoComposeResolvedOutputFileName));
        OnPropertyChanged(nameof(AudioVideoComposeOutputNameHintText));
        OnPropertyChanged(nameof(TimelineHintText));
        OnPropertyChanged(nameof(VideoTrackEmptyText));
        OnPropertyChanged(nameof(AudioTrackEmptyText));
        OnPropertyChanged(nameof(VideoJoinTimelineVisibility));
        OnPropertyChanged(nameof(AudioJoinTimelineVisibility));
        OnPropertyChanged(nameof(StandardTimelineVisibility));
        RaiseTrackStatePropertiesChanged();
    }

    private IReadOnlyList<OutputFormatOption> BuildVideoJoinOutputFormats() =>
        _configuration.SupportedVideoOutputFormats
            .Select(LocalizeOutputFormat)
            .ToArray();

    private IReadOnlyList<OutputFormatOption> BuildAudioJoinOutputFormats() =>
        _configuration.SupportedAudioOutputFormats
            .Select(LocalizeOutputFormat)
            .ToArray();

    private OutputFormatOption LocalizeOutputFormat(OutputFormatOption option) =>
        _localizationService is null
            ? option
            : option.Localize(_localizationService);
}
