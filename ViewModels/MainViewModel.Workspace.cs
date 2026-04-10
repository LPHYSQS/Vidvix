using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using Microsoft.UI.Xaml;
using Vidvix.Core.Models;

namespace Vidvix.ViewModels;

public sealed partial class MainViewModel
{
    private const string AudioProcessingModeDisplayName = "\u97f3\u9891\u683c\u5f0f\u8f6c\u6362";
    private const string AudioProcessingModeDescription = "\u5c06\u97f3\u9891\u6587\u4ef6\u8f6c\u6362\u4e3a\u76ee\u6807\u683c\u5f0f\uff0c\u652f\u6301\u591a\u79cd\u97f3\u9891\u683c\u5f0f\u4e4b\u95f4\u4e92\u76f8\u8f6c\u6362\u3002";

    public ObservableCollection<LogEntry> LogEntries => GetCurrentLogEntries();

    public ObservableCollection<MediaJobViewModel> ImportItems => GetCurrentImportItems();

    public bool IsVideoWorkspaceSelected => _selectedWorkspaceKind == ProcessingWorkspaceKind.Video;

    public bool IsAudioWorkspaceSelected => _selectedWorkspaceKind == ProcessingWorkspaceKind.Audio;

    public Visibility VideoProcessingModeVisibility => IsVideoWorkspaceSelected ? Visibility.Visible : Visibility.Collapsed;

    public Visibility AudioProcessingModeVisibility => IsAudioWorkspaceSelected ? Visibility.Visible : Visibility.Collapsed;

    public string QueueDragDropHintText => $"\u652f\u6301\u62d6\u62fd{GetCurrentMediaFileLabel()}\u6216\u6587\u4ef6\u5939\u5230\u7a97\u53e3\u4efb\u610f\u4f4d\u7f6e";

    public string DragDropCaptionText => GetDragDropCaptionText();

    public string FixedProcessingModeDisplayName => AudioProcessingModeDisplayName;

    public string FixedProcessingModeDescription => AudioProcessingModeDescription;

    private bool IsAudioWorkspace => _selectedWorkspaceKind == ProcessingWorkspaceKind.Audio;

    private ObservableCollection<MediaJobViewModel> GetCurrentImportItems() =>
        IsAudioWorkspace ? _audioImportItems : _videoImportItems;

    private ObservableCollection<LogEntry> GetCurrentLogEntries() =>
        IsAudioWorkspace ? _audioLogEntries : _videoLogEntries;

    private IReadOnlyList<string> GetCurrentSupportedInputFileTypes() =>
        IsAudioWorkspace ? _configuration.SupportedAudioInputFileTypes : _configuration.SupportedVideoInputFileTypes;

    private string GetCurrentMediaLabel() => IsAudioWorkspace ? "\u97f3\u9891" : "\u89c6\u9891";

    private string GetCurrentMediaFileLabel() => IsAudioWorkspace ? "\u97f3\u9891\u6587\u4ef6" : "\u89c6\u9891\u6587\u4ef6";

    private string GetReadyForImportMessage() => $"\u8bf7\u5bfc\u5165{GetCurrentMediaFileLabel()}\u6216\u6587\u4ef6\u5939\u3002";

    private string GetEmptyQueueProcessingMessage() => $"\u8bf7\u5148\u5bfc\u5165\u81f3\u5c11\u4e00\u4e2a{GetCurrentMediaFileLabel()}\u3002";

    private string GetImportFilePickerCommitText() => $"\u5bfc\u5165{GetCurrentMediaFileLabel()}";

    private string GetImportFolderPickerCommitText() => $"\u5bfc\u5165{GetCurrentMediaLabel()}\u6587\u4ef6\u5939";

    private string GetDragDropCaptionText() => $"\u5bfc\u5165{GetCurrentMediaFileLabel()}\u6216\u6587\u4ef6\u5939";

    private string CreateImportedCountMessage(int addedCount) => $"\u5df2\u5bfc\u5165 {addedCount} \u4e2a{GetCurrentMediaFileLabel()}\u3002";

    private string CreateNoProcessableImportMessage() => $"\u6ca1\u6709\u53d1\u73b0\u53ef\u5904\u7406\u7684{GetCurrentMediaFileLabel()}\u3002";

    private ProcessingMode GetCurrentOutputFormatPreferenceMode() =>
        IsAudioWorkspace ? ProcessingMode.AudioTrackExtract : SelectedProcessingMode.Mode;

    private ProcessingWorkspaceKind ResolvePreferredWorkspaceKind(ProcessingWorkspaceKind preferredWorkspaceKind) =>
        Enum.IsDefined(typeof(ProcessingWorkspaceKind), preferredWorkspaceKind)
            ? preferredWorkspaceKind
            : ProcessingWorkspaceKind.Video;

    private void SwitchToVideoWorkspace() => SetWorkspace(ProcessingWorkspaceKind.Video);

    private void SwitchToAudioWorkspace() => SetWorkspace(ProcessingWorkspaceKind.Audio);

    private void SetWorkspace(ProcessingWorkspaceKind workspaceKind)
    {
        if (_selectedWorkspaceKind == workspaceKind)
        {
            return;
        }

        _selectedWorkspaceKind = workspaceKind;
        _pendingDetailItem = null;
        CloseMediaDetails();

        OnPropertyChanged(nameof(ImportItems));
        OnPropertyChanged(nameof(LogEntries));
        OnPropertyChanged(nameof(IsVideoWorkspaceSelected));
        OnPropertyChanged(nameof(IsAudioWorkspaceSelected));
        OnPropertyChanged(nameof(VideoProcessingModeVisibility));
        OnPropertyChanged(nameof(AudioProcessingModeVisibility));
        OnPropertyChanged(nameof(QueueSummaryText));
        OnPropertyChanged(nameof(SupportedInputFormatsHint));
        OnPropertyChanged(nameof(QueueDragDropHintText));
        OnPropertyChanged(nameof(DragDropCaptionText));
        OnPropertyChanged(nameof(FixedProcessingModeDisplayName));
        OnPropertyChanged(nameof(FixedProcessingModeDescription));

        ReloadOutputFormats();
        RecalculatePlannedOutputs();
        NotifyCommandStates();
        PersistUserPreferences();

        if (!IsBusy && !string.IsNullOrWhiteSpace(_runtimeExecutablePath) && File.Exists(_runtimeExecutablePath))
        {
            SetReadyStatusMessage();
        }
    }
}
