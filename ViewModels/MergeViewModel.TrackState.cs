using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using Vidvix.Core.Models;

namespace Vidvix.ViewModels;

public sealed partial class MergeViewModel
{
    private TrackItem? GetEffectiveVideoResolutionPresetItem()
    {
        if (TryResolveManualVideoResolutionPresetItem(out var manualPresetTrackItem))
        {
            return manualPresetTrackItem;
        }

        return _videoJoinVideoTrackItems.FirstOrDefault(trackItem => trackItem.IsSourceAvailable);
    }

    private TrackItem? GetEffectiveAudioParameterPresetItem()
    {
        if (TryResolveManualAudioParameterPresetItem(out var manualPresetTrackItem))
        {
            return manualPresetTrackItem;
        }

        return _audioJoinAudioTrackItems.FirstOrDefault(trackItem => trackItem.IsSourceAvailable);
    }

    private bool HasManualVideoResolutionPresetSelection() =>
        TryResolveManualVideoResolutionPresetItem(out _);

    private bool HasManualAudioParameterPresetSelection() =>
        TryResolveManualAudioParameterPresetItem(out _);

    private bool TryResolveManualVideoResolutionPresetItem(out TrackItem? trackItem)
    {
        trackItem = null;
        if (_manualVideoResolutionPresetTrackId is not Guid presetTrackId)
        {
            return false;
        }

        trackItem = _videoJoinVideoTrackItems.FirstOrDefault(candidate =>
            candidate.TrackId == presetTrackId &&
            candidate.IsSourceAvailable);
        return trackItem is not null;
    }

    private bool TryResolveManualAudioParameterPresetItem(out TrackItem? trackItem)
    {
        trackItem = null;
        if (_manualAudioParameterPresetTrackId is not Guid presetTrackId)
        {
            return false;
        }

        trackItem = _audioJoinAudioTrackItems.FirstOrDefault(candidate =>
            candidate.TrackId == presetTrackId &&
            candidate.IsSourceAvailable);
        return trackItem is not null;
    }

    private void OnMediaItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        SynchronizeTrackCollectionsWithMediaSources();
        OnPropertyChanged(nameof(MediaItemsEmptyVisibility));
        NotifyCommandStates();
    }

    private void OnTrackItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (sender is ObservableCollection<TrackItem> trackItems)
        {
            if (IsAudioVideoComposeTrackCollection(trackItems))
            {
                NormalizeAudioVideoComposeTrackCollection(trackItems);
            }

            RefreshTrackCollection(
                trackItems,
                supportsVideoPreset: ReferenceEquals(trackItems, _videoJoinVideoTrackItems));
        }

        RaiseTrackStatePropertiesChanged();
        NotifyCommandStates();
    }

    private void SynchronizeTrackCollectionsWithMediaSources()
    {
        var availableSourcePaths = new HashSet<string>(
            _mediaItems
                .Select(item => item.SourcePath)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(NormalizeSourcePath),
            StringComparer.OrdinalIgnoreCase);

        foreach (var trackItem in GetAllTrackCollections().SelectMany(collection => collection))
        {
            trackItem.IsSourceAvailable = string.IsNullOrWhiteSpace(trackItem.SourcePath) ||
                                          availableSourcePaths.Contains(NormalizeSourcePath(trackItem.SourcePath));
        }

        RefreshTrackCollection(_videoJoinVideoTrackItems, supportsVideoPreset: true);
        RefreshTrackCollection(_audioJoinAudioTrackItems, supportsVideoPreset: false);
        NormalizeAudioVideoComposeTrackCollection(_audioVideoComposeVideoTrackItems);
        NormalizeAudioVideoComposeTrackCollection(_audioVideoComposeAudioTrackItems);
        RefreshTrackCollection(_audioVideoComposeVideoTrackItems, supportsVideoPreset: false);
        RefreshTrackCollection(_audioVideoComposeAudioTrackItems, supportsVideoPreset: false);
        RaiseTrackStatePropertiesChanged();
    }

    private bool IsAudioVideoComposeTrackCollection(ObservableCollection<TrackItem> trackItems) =>
        ReferenceEquals(trackItems, _audioVideoComposeVideoTrackItems) ||
        ReferenceEquals(trackItems, _audioVideoComposeAudioTrackItems);

    private void NormalizeAudioVideoComposeTrackCollection(ObservableCollection<TrackItem> trackItems)
    {
        if (_isNormalizingAudioVideoComposeTrackCollection ||
            !IsAudioVideoComposeTrackCollection(trackItems) ||
            trackItems.Count <= 1)
        {
            return;
        }

        _isNormalizingAudioVideoComposeTrackCollection = true;
        try
        {
            while (trackItems.Count > 1)
            {
                trackItems.RemoveAt(0);
            }
        }
        finally
        {
            _isNormalizingAudioVideoComposeTrackCollection = false;
        }
    }

    private void RefreshTrackCollection(
        ObservableCollection<TrackItem> trackItems,
        bool supportsVideoPreset)
    {
        var presetTrackItem = ReferenceEquals(trackItems, _videoJoinVideoTrackItems)
            ? GetEffectiveVideoResolutionPresetItem()
            : ReferenceEquals(trackItems, _audioJoinAudioTrackItems)
                ? _selectedAudioJoinParameterMode == AudioJoinParameterMode.Preset
                    ? GetEffectiveAudioParameterPresetItem()
                    : null
                : ReferenceEquals(trackItems, _audioVideoComposeVideoTrackItems)
                    ? GetAudioVideoComposeVideoTrackItem() is not null &&
                      _selectedAudioVideoComposeReferenceMode == AudioVideoComposeReferenceMode.Video
                        ? GetAudioVideoComposeVideoTrackItem()
                        : null
                    : ReferenceEquals(trackItems, _audioVideoComposeAudioTrackItems)
                        ? GetAudioVideoComposeAudioTrackItem() is not null &&
                          _selectedAudioVideoComposeReferenceMode == AudioVideoComposeReferenceMode.Audio
                            ? GetAudioVideoComposeAudioTrackItem()
                            : null
                : null;
        for (var index = 0; index < trackItems.Count; index++)
        {
            var trackItem = trackItems[index];
            trackItem.SequenceNumber = index + 1;
            trackItem.IsResolutionPreset = presetTrackItem is not null && ReferenceEquals(trackItem, presetTrackItem);
        }
    }

    private void RaiseTrackStatePropertiesChanged()
    {
        OnPropertyChanged(nameof(VideoTrackSummaryText));
        OnPropertyChanged(nameof(VideoJoinTotalDurationText));
        OnPropertyChanged(nameof(AudioTrackSummaryText));
        OnPropertyChanged(nameof(AudioJoinTotalDurationText));
        OnPropertyChanged(nameof(VideoTrackEmptyVisibility));
        OnPropertyChanged(nameof(AudioTrackEmptyVisibility));
        OnPropertyChanged(nameof(VideoResolutionPresetSummaryText));
        OnPropertyChanged(nameof(AudioParameterPresetSummaryText));
        OnPropertyChanged(nameof(VideoJoinOutputDirectoryDisplayText));
        OnPropertyChanged(nameof(AudioJoinOutputDirectoryDisplayText));
        OnPropertyChanged(nameof(VideoJoinResolvedOutputFileName));
        OnPropertyChanged(nameof(VideoJoinOutputNameHintText));
        OnPropertyChanged(nameof(AudioJoinResolvedOutputFileName));
        OnPropertyChanged(nameof(AudioJoinOutputNameHintText));
        OnPropertyChanged(nameof(AudioTrackOperationHintText));
        OnPropertyChanged(nameof(AudioJoinParameterModeHintText));
        OnPropertyChanged(nameof(AudioJoinPresetSelectionVisibility));
        RaiseAudioVideoComposeStatePropertiesChanged();
    }

    private void SetModeMismatchWarningVisibility(bool isVisible)
    {
        if (_isModeMismatchWarningVisible != isVisible)
        {
            _isModeMismatchWarningVisible = isVisible;
            OnPropertyChanged(nameof(ModeMismatchWarningVisibility));
        }
    }

    private void SetModeMismatchWarningVisibility(
        bool isVisible,
        string key,
        string fallback,
        params (string Name, object? Value)[] arguments)
    {
        _modeMismatchWarningMessageState = new LocalizedTextState(key, fallback, arguments);
        UpdateModeMismatchWarningMessage(ResolveLocalizedText(_modeMismatchWarningMessageState));
        SetModeMismatchWarningVisibility(isVisible);
    }

    private void ClearModeMismatchWarningMessageLocalizationState() => _modeMismatchWarningMessageState = null;

    private void ClearModeMismatchWarning()
    {
        ClearModeMismatchWarningMessageLocalizationState();
        UpdateModeMismatchWarningMessage(string.Empty);
        SetModeMismatchWarningVisibility(false);
    }

    private void UpdateModeMismatchWarningMessage(string message)
    {
        if (_modeMismatchWarningMessage != message)
        {
            _modeMismatchWarningMessage = message;
            OnPropertyChanged(nameof(ModeMismatchWarningMessage));
        }
    }

    private bool TryResolveTrackCollectionForAddition(
        MediaItem mediaItem,
        out ObservableCollection<TrackItem> trackItems,
        out string rejectionMessage)
    {
        var profile = CurrentModeState.Profile;
        rejectionMessage = string.Empty;

        if (mediaItem.IsVideo)
        {
            if (!profile.SupportsVideoTrackInput)
            {
                trackItems = _emptyTrackItems;
                rejectionMessage = profile.RejectVideoInputMessage;
                return false;
            }

            trackItems = CurrentModeState.VideoTrackItems;
            return true;
        }

        if (!profile.SupportsAudioTrackInput)
        {
            trackItems = _emptyTrackItems;
            rejectionMessage = profile.RejectAudioInputMessage;
            return false;
        }

        trackItems = CurrentModeState.AudioTrackItems;
        return true;
    }

    private IReadOnlyDictionary<MergeWorkspaceMode, MergeWorkspaceModeState> CreateModeStates(
        ApplicationConfiguration configuration)
    {
        var profiles = configuration.MergeModeProfiles.ToDictionary(
            pair => pair.Key,
            pair => _localizationService is null
                ? pair.Value
                : pair.Value.Localize(_localizationService));

        return new Dictionary<MergeWorkspaceMode, MergeWorkspaceModeState>
        {
            [MergeWorkspaceMode.VideoJoin] = new(
                profiles[MergeWorkspaceMode.VideoJoin],
                _videoJoinVideoTrackItems,
                _emptyTrackItems),
            [MergeWorkspaceMode.AudioJoin] = new(
                profiles[MergeWorkspaceMode.AudioJoin],
                _emptyTrackItems,
                _audioJoinAudioTrackItems),
            [MergeWorkspaceMode.AudioVideoCompose] = new(
                profiles[MergeWorkspaceMode.AudioVideoCompose],
                _audioVideoComposeVideoTrackItems,
                _audioVideoComposeAudioTrackItems)
        };
    }

    private MergeWorkspaceModeState GetModeState(MergeWorkspaceMode mergeMode) =>
        _modeStates.TryGetValue(mergeMode, out var state)
            ? state
            : _modeStates[MergeWorkspaceMode.AudioVideoCompose];

    private IEnumerable<ObservableCollection<TrackItem>> GetAllTrackCollections()
    {
        yield return _videoJoinVideoTrackItems;
        yield return _audioJoinAudioTrackItems;
        yield return _audioVideoComposeVideoTrackItems;
        yield return _audioVideoComposeAudioTrackItems;
    }

    private bool IsSourcePathAvailable(string sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            return true;
        }

        var normalizedSourcePath = NormalizeSourcePath(sourcePath);
        return _mediaItems.Any(item => IsSameSource(item.SourcePath, normalizedSourcePath));
    }

    private static string NormalizeSourcePath(string sourcePath) =>
        string.IsNullOrWhiteSpace(sourcePath)
            ? string.Empty
            : Path.GetFullPath(sourcePath);

    private static bool IsSameSource(string sourcePath, string normalizedSourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || string.IsNullOrWhiteSpace(normalizedSourcePath))
        {
            return false;
        }

        return string.Equals(
            NormalizeSourcePath(sourcePath),
            normalizedSourcePath,
            StringComparison.OrdinalIgnoreCase);
    }
}
