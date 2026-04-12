using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using Microsoft.UI.Xaml;
using Vidvix.Core.Models;

namespace Vidvix.ViewModels;

public sealed partial class MainViewModel
{
    public ObservableCollection<LogEntry> LogEntries => GetCurrentLogEntries();

    public ObservableCollection<MediaJobViewModel> ImportItems => GetCurrentImportItems();

    public bool IsVideoWorkspaceSelected => _selectedWorkspaceKind == ProcessingWorkspaceKind.Video;

    public bool IsAudioWorkspaceSelected => _selectedWorkspaceKind == ProcessingWorkspaceKind.Audio;

    public bool IsTrimWorkspaceSelected => _selectedWorkspaceKind == ProcessingWorkspaceKind.Trim;

    public Visibility ProcessingWorkspaceVisibility => IsTrimWorkspaceSelected ? Visibility.Collapsed : Visibility.Visible;

    public Visibility TrimWorkspaceVisibility => IsTrimWorkspaceSelected ? Visibility.Visible : Visibility.Collapsed;

    public Visibility VideoProcessingModeVisibility => IsVideoWorkspaceSelected ? Visibility.Visible : Visibility.Collapsed;

    public Visibility AudioProcessingModeVisibility => IsAudioWorkspaceSelected ? Visibility.Visible : Visibility.Collapsed;

    public string QueueDragDropHintText => GetCurrentWorkspaceProfile().QueueDragDropHintText;

    public string DragDropCaptionText => GetCurrentWorkspaceProfile().DragDropCaptionText;

    public string FixedProcessingModeDisplayName => GetWorkspaceProfile(ProcessingWorkspaceKind.Audio).FixedProcessingModeDisplayName;

    public string FixedProcessingModeDescription => GetWorkspaceProfile(ProcessingWorkspaceKind.Audio).FixedProcessingModeDescription;

    private bool IsAudioWorkspace => _selectedWorkspaceKind == ProcessingWorkspaceKind.Audio;

    private bool IsTrimWorkspace => _selectedWorkspaceKind == ProcessingWorkspaceKind.Trim;

    private ProcessingWorkspaceProfile GetCurrentWorkspaceProfile() =>
        GetWorkspaceProfile(_selectedWorkspaceKind);

    private ProcessingWorkspaceProfile GetWorkspaceProfile(ProcessingWorkspaceKind workspaceKind) =>
        _configuration.WorkspaceProfiles.TryGetValue(workspaceKind, out var profile)
            ? profile
            : _configuration.WorkspaceProfiles[ProcessingWorkspaceKind.Video];

    private ObservableCollection<MediaJobViewModel> GetCurrentImportItems() =>
        GetImportItems(_selectedWorkspaceKind);

    private ObservableCollection<MediaJobViewModel> GetImportItems(ProcessingWorkspaceKind workspaceKind) =>
        workspaceKind switch
        {
            ProcessingWorkspaceKind.Audio => _audioImportItems,
            ProcessingWorkspaceKind.Trim => _trimImportItems,
            _ => _videoImportItems
        };

    private ObservableCollection<LogEntry> GetCurrentLogEntries() =>
        GetLogEntries(_selectedWorkspaceKind);

    private ObservableCollection<LogEntry> GetLogEntries(ProcessingWorkspaceKind workspaceKind) =>
        workspaceKind switch
        {
            ProcessingWorkspaceKind.Audio => _audioLogEntries,
            ProcessingWorkspaceKind.Trim => _trimLogEntries,
            _ => _videoLogEntries
        };

    private IReadOnlyList<string> GetCurrentSupportedInputFileTypes() =>
        GetSupportedInputFileTypes(_selectedWorkspaceKind);

    private IReadOnlyList<string> GetSupportedInputFileTypes(ProcessingWorkspaceKind workspaceKind) =>
        GetWorkspaceProfile(workspaceKind).SupportedInputFileTypes;

    private string GetCurrentMediaLabel() => GetCurrentWorkspaceProfile().MediaLabel;

    private string GetMediaLabel(ProcessingWorkspaceKind workspaceKind) => GetWorkspaceProfile(workspaceKind).MediaLabel;

    private string GetCurrentMediaFileLabel() => GetCurrentWorkspaceProfile().MediaFileLabel;

    private string GetMediaFileLabel(ProcessingWorkspaceKind workspaceKind) => GetWorkspaceProfile(workspaceKind).MediaFileLabel;

    private string GetReadyForImportMessage() => GetCurrentWorkspaceProfile().ReadyForImportMessage;

    private string GetEmptyQueueProcessingMessage() => GetCurrentWorkspaceProfile().EmptyQueueProcessingMessage;

    private string GetImportFilePickerCommitText() => GetCurrentWorkspaceProfile().ImportFilePickerCommitText;

    private string GetImportFolderPickerCommitText() => GetCurrentWorkspaceProfile().ImportFolderPickerCommitText;

    private string CreateImportedCountMessage(int addedCount) => GetCurrentWorkspaceProfile().CreateImportedCountMessage(addedCount);

    private string CreateNoProcessableImportMessage() => GetCurrentWorkspaceProfile().NoProcessableImportMessage;

    private ProcessingMode GetCurrentOutputFormatPreferenceMode() =>
        IsTrimWorkspace
            ? ProcessingMode.VideoConvert
            : IsAudioWorkspace
                ? ProcessingMode.AudioTrackExtract
                : SelectedProcessingMode.Mode;

    private ProcessingWorkspaceKind ResolvePreferredWorkspaceKind(ProcessingWorkspaceKind preferredWorkspaceKind) =>
        Enum.IsDefined(typeof(ProcessingWorkspaceKind), preferredWorkspaceKind)
            ? preferredWorkspaceKind
            : ProcessingWorkspaceKind.Video;

    private void SwitchToVideoWorkspace() => SetWorkspace(ProcessingWorkspaceKind.Video);

    private void SwitchToAudioWorkspace() => SetWorkspace(ProcessingWorkspaceKind.Audio);

    private void SwitchToTrimWorkspace() => SetWorkspace(ProcessingWorkspaceKind.Trim);

    private void SetWorkspace(ProcessingWorkspaceKind workspaceKind)
    {
        if (_selectedWorkspaceKind == workspaceKind)
        {
            return;
        }

        if (IsBusy || TrimWorkspace.IsBusy)
        {
            StatusMessage = "当前任务处理中，暂不支持切换模块。";
            return;
        }

        _selectedWorkspaceKind = workspaceKind;
        _pendingDetailItem = null;
        CloseMediaDetails();

        OnPropertyChanged(nameof(ImportItems));
        OnPropertyChanged(nameof(LogEntries));
        OnPropertyChanged(nameof(IsVideoWorkspaceSelected));
        OnPropertyChanged(nameof(IsAudioWorkspaceSelected));
        OnPropertyChanged(nameof(IsTrimWorkspaceSelected));
        OnPropertyChanged(nameof(ProcessingWorkspaceVisibility));
        OnPropertyChanged(nameof(TrimWorkspaceVisibility));
        OnPropertyChanged(nameof(VideoProcessingModeVisibility));
        OnPropertyChanged(nameof(AudioProcessingModeVisibility));
        OnPropertyChanged(nameof(QueueSummaryText));
        OnPropertyChanged(nameof(SupportedInputFormatsHint));
        OnPropertyChanged(nameof(QueueDragDropHintText));
        OnPropertyChanged(nameof(DragDropCaptionText));
        OnPropertyChanged(nameof(FixedProcessingModeDisplayName));
        OnPropertyChanged(nameof(FixedProcessingModeDescription));

        if (!IsTrimWorkspaceSelected)
        {
            ReloadOutputFormats();
            RecalculatePlannedOutputs();
        }

        NotifyCommandStates();
        PersistUserPreferences();

        if (!IsBusy && !string.IsNullOrWhiteSpace(_runtimeExecutablePath) && File.Exists(_runtimeExecutablePath))
        {
            SetReadyStatusMessage();
        }
    }
}
