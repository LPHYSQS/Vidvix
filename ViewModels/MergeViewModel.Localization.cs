using System;
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
        RefreshLocalizedItemText();
        RaiseUiTextPropertiesChanged();
        RaiseTrackStatePropertiesChanged();
        RefreshLocalizedRuntimeText();
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

    private string GetLocalizedText(string key, string fallback) =>
        _localizationService?.GetString(key, fallback) ?? fallback;

    private string FormatLocalizedText(
        string key,
        string fallback,
        params (string Name, object? Value)[] arguments)
    {
        if (_localizationService is null)
        {
            var formattedFallback = fallback;
            foreach (var argument in arguments)
            {
                var resolvedValue = ResolveLocalizedArgumentValue(argument.Value);
                formattedFallback = formattedFallback.Replace(
                    $"{{{argument.Name}}}",
                    resolvedValue?.ToString() ?? string.Empty,
                    StringComparison.Ordinal);
            }

            return formattedFallback;
        }

        Dictionary<string, object?>? localizedArguments = null;
        if (arguments.Length > 0)
        {
            localizedArguments = new Dictionary<string, object?>(arguments.Length, StringComparer.Ordinal);
            foreach (var argument in arguments)
            {
                localizedArguments[argument.Name] = ResolveLocalizedArgumentValue(argument.Value);
            }
        }

        return _localizationService.Format(key, localizedArguments, fallback);
    }

    private static (string Name, object? Value) LocalizedArgument(string name, object? value) => (name, value);

    private static (string Name, object? Value) LocalizedArgument(string name, Func<object?> resolver) =>
        (name, resolver);

    private object? ResolveLocalizedArgumentValue(object? value) =>
        value switch
        {
            Func<object?> resolver => resolver(),
            LocalizedTextState state => ResolveLocalizedText(state),
            _ => value
        };

    private void SetStatusMessage(string key, string fallback, params (string Name, object? Value)[] arguments)
    {
        _statusMessageState = new LocalizedTextState(key, fallback, arguments);
        SetProperty(ref _statusMessage, ResolveLocalizedText(_statusMessageState), nameof(StatusMessage));
    }

    private void SetStatusMessage(LocalizedTextState state)
    {
        _statusMessageState = state;
        SetProperty(ref _statusMessage, ResolveLocalizedText(_statusMessageState), nameof(StatusMessage));
    }

    private void SetProcessingLockedStatusMessage(
        string moduleKey,
        string moduleFallback,
        string operationKey,
        string operationFallback) =>
        SetStatusMessage(
            "merge.status.processingLocked.moduleOperation",
            "当前{module}任务处理中，若需{operation}，请先取消当前任务。",
            LocalizedArgument("module", () => GetLocalizedText(moduleKey, moduleFallback)),
            LocalizedArgument("operation", () => GetLocalizedText(operationKey, operationFallback)));

    private void ClearStatusMessageLocalizationState() => _statusMessageState = null;

    private void RefreshLocalizedRuntimeText()
    {
        RefreshLocalizedTextState(
            _statusMessageState,
            value => SetProperty(ref _statusMessage, value, nameof(StatusMessage)));
        RefreshLocalizedTextState(_modeMismatchWarningMessageState, UpdateModeMismatchWarningMessage);
        RefreshLocalizedTextState(
            _processingProgressSummaryTextState,
            value => SetProperty(ref _processingProgressSummaryText, value, nameof(ProcessingProgressSummaryText)));
        RefreshLocalizedTextState(
            _processingProgressDetailTextState,
            value => SetProperty(ref _processingProgressDetailText, value, nameof(ProcessingProgressDetailText)));
        RefreshLocalizedTextState(
            _processingProgressPercentTextState,
            value => SetProperty(ref _processingProgressPercentText, value, nameof(ProcessingProgressPercentText)));
    }

    private void RefreshLocalizedTextState(LocalizedTextState? state, Action<string> apply)
    {
        if (state is null)
        {
            return;
        }

        apply(ResolveLocalizedText(state));
    }

    private void RefreshLocalizedItemText()
    {
        foreach (var mediaItem in _mediaItems)
        {
            mediaItem.RefreshLocalization();
        }

        foreach (var trackItem in _videoJoinVideoTrackItems)
        {
            trackItem.RefreshLocalization();
        }

        foreach (var trackItem in _audioJoinAudioTrackItems)
        {
            trackItem.RefreshLocalization();
        }

        foreach (var trackItem in _audioVideoComposeVideoTrackItems)
        {
            trackItem.RefreshLocalization();
        }

        foreach (var trackItem in _audioVideoComposeAudioTrackItems)
        {
            trackItem.RefreshLocalization();
        }

        foreach (var trackItem in _emptyTrackItems)
        {
            trackItem.RefreshLocalization();
        }
    }

    private string ResolveLocalizedText(LocalizedTextState state) =>
        state.Arguments.Length == 0
            ? GetLocalizedText(state.Key, state.Fallback)
            : FormatLocalizedText(state.Key, state.Fallback, state.Arguments);

    private sealed class LocalizedTextState
    {
        public LocalizedTextState(string key, string fallback, params (string Name, object? Value)[] arguments)
        {
            Key = key;
            Fallback = fallback;
            Arguments = arguments ?? Array.Empty<(string Name, object? Value)>();
        }

        public string Key { get; }

        public string Fallback { get; }

        public (string Name, object? Value)[] Arguments { get; }
    }
}
