using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
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

    public bool IsMergeWorkspaceSelected => _selectedWorkspaceKind == ProcessingWorkspaceKind.Merge;

    public bool IsTerminalWorkspaceSelected => _selectedWorkspaceKind == ProcessingWorkspaceKind.Terminal;

    public Visibility ProcessingWorkspaceVisibility =>
        IsVideoWorkspaceSelected || IsAudioWorkspaceSelected ? Visibility.Visible : Visibility.Collapsed;

    public Visibility TrimWorkspaceVisibility => IsTrimWorkspaceSelected ? Visibility.Visible : Visibility.Collapsed;

    public Visibility MergeWorkspaceVisibility => IsMergeWorkspaceSelected ? Visibility.Visible : Visibility.Collapsed;

    public Visibility TerminalWorkspaceVisibility => IsTerminalWorkspaceSelected ? Visibility.Visible : Visibility.Collapsed;

    public string SettingsPaneDescription =>
        IsTerminalWorkspaceSelected
            ? "终端模块当前仅提供外观设置，方便快速切换软件主题。"
            : "应用级偏好统一集中在这里扩展，不占用主处理区域。";

    public Visibility SettingsPaneProcessingBehaviorVisibility =>
        IsTerminalWorkspaceSelected ? Visibility.Collapsed : Visibility.Visible;

    public Visibility SettingsPaneTranscodingVisibility =>
        IsTerminalWorkspaceSelected ? Visibility.Collapsed : Visibility.Visible;

    public Visibility VideoProcessingModeVisibility => IsVideoWorkspaceSelected ? Visibility.Visible : Visibility.Collapsed;

    public Visibility AudioProcessingModeVisibility => IsAudioWorkspaceSelected ? Visibility.Visible : Visibility.Collapsed;

    public string WorkspaceHeaderCaption => "当前模块";

    public string WorkspaceHeaderTitle => GetCurrentWorkspaceProfile().HeaderTitle;

    public string WorkspaceHeaderDescription => GetCurrentWorkspaceProfile().HeaderDescription;

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
            ProcessingWorkspaceKind.Merge => _mergeImportItems,
            ProcessingWorkspaceKind.Terminal => _terminalImportItems,
            _ => _videoImportItems
        };

    private ObservableCollection<LogEntry> GetCurrentLogEntries() =>
        GetLogEntries(_selectedWorkspaceKind);

    private ObservableCollection<LogEntry> GetLogEntries(ProcessingWorkspaceKind workspaceKind) =>
        workspaceKind switch
        {
            ProcessingWorkspaceKind.Audio => _audioLogEntries,
            ProcessingWorkspaceKind.Trim => _trimLogEntries,
            ProcessingWorkspaceKind.Merge => _mergeLogEntries,
            ProcessingWorkspaceKind.Terminal => _terminalLogEntries,
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

    private Task SwitchToVideoWorkspaceAsync() => SetWorkspaceAsync(ProcessingWorkspaceKind.Video);

    private Task SwitchToAudioWorkspaceAsync() => SetWorkspaceAsync(ProcessingWorkspaceKind.Audio);

    private Task SwitchToTrimWorkspaceAsync() => SetWorkspaceAsync(ProcessingWorkspaceKind.Trim);

    private Task SwitchToMergeWorkspaceAsync() => SetWorkspaceAsync(ProcessingWorkspaceKind.Merge);

    private Task SwitchToTerminalWorkspaceAsync() => SetWorkspaceAsync(ProcessingWorkspaceKind.Terminal);

    private async Task SetWorkspaceAsync(ProcessingWorkspaceKind workspaceKind)
    {
        if (_selectedWorkspaceKind == workspaceKind)
        {
            return;
        }

        if (IsBusy || TrimWorkspace.IsBusy || MergeWorkspace.IsVideoJoinProcessing)
        {
            StatusMessage = "当前任务处理中，暂不支持切换模块。";
            return;
        }

        if (_selectedWorkspaceKind == ProcessingWorkspaceKind.Trim &&
            workspaceKind != ProcessingWorkspaceKind.Trim)
        {
            try
            {
                await TrimWorkspace.PausePreviewForDeactivationAsync();
            }
            catch (Exception exception)
            {
                _logger.Log(LogLevel.Warning, "离开裁剪模块时暂停预览失败，继续切换模块。", exception);
            }
        }

        _selectedWorkspaceKind = workspaceKind;
        _pendingDetailItem = null;
        CloseMediaDetails();

        OnPropertyChanged(nameof(ImportItems));
        OnPropertyChanged(nameof(LogEntries));
        OnPropertyChanged(nameof(IsVideoWorkspaceSelected));
        OnPropertyChanged(nameof(IsAudioWorkspaceSelected));
        OnPropertyChanged(nameof(IsTrimWorkspaceSelected));
        OnPropertyChanged(nameof(IsMergeWorkspaceSelected));
        OnPropertyChanged(nameof(IsTerminalWorkspaceSelected));
        OnPropertyChanged(nameof(ProcessingWorkspaceVisibility));
        OnPropertyChanged(nameof(TrimWorkspaceVisibility));
        OnPropertyChanged(nameof(MergeWorkspaceVisibility));
        OnPropertyChanged(nameof(TerminalWorkspaceVisibility));
        OnPropertyChanged(nameof(VideoProcessingModeVisibility));
        OnPropertyChanged(nameof(AudioProcessingModeVisibility));
        OnPropertyChanged(nameof(WorkspaceHeaderTitle));
        OnPropertyChanged(nameof(WorkspaceHeaderDescription));
        OnPropertyChanged(nameof(QueueSummaryText));
        OnPropertyChanged(nameof(SupportedInputFormatsHint));
        OnPropertyChanged(nameof(QueueDragDropHintText));
        OnPropertyChanged(nameof(DragDropCaptionText));
        OnPropertyChanged(nameof(SettingsPaneDescription));
        OnPropertyChanged(nameof(SettingsPaneProcessingBehaviorVisibility));
        OnPropertyChanged(nameof(SettingsPaneTranscodingVisibility));
        OnPropertyChanged(nameof(FixedProcessingModeDisplayName));
        OnPropertyChanged(nameof(FixedProcessingModeDescription));

        if (IsVideoWorkspaceSelected || IsAudioWorkspaceSelected)
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
