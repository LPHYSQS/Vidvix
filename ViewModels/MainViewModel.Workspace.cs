using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Vidvix.Core.Models;

namespace Vidvix.ViewModels;

public sealed partial class MainViewModel
{
    private IReadOnlyDictionary<ProcessingWorkspaceKind, ProcessingWorkspaceProfile> _workspaceProfiles =
        new Dictionary<ProcessingWorkspaceKind, ProcessingWorkspaceProfile>();

    public ObservableCollection<LogEntry> LogEntries => GetCurrentLogEntries();

    public ObservableCollection<MediaJobViewModel> ImportItems => GetCurrentImportItems();

    public bool IsVideoWorkspaceSelected => _selectedWorkspaceKind == ProcessingWorkspaceKind.Video;

    public bool IsAudioWorkspaceSelected => _selectedWorkspaceKind == ProcessingWorkspaceKind.Audio;

    public bool IsTrimWorkspaceSelected => _selectedWorkspaceKind == ProcessingWorkspaceKind.Trim;

    public bool IsMergeWorkspaceSelected => _selectedWorkspaceKind == ProcessingWorkspaceKind.Merge;

    public bool IsSplitAudioWorkspaceSelected => _selectedWorkspaceKind == ProcessingWorkspaceKind.SplitAudio;

    public bool IsTerminalWorkspaceSelected => _selectedWorkspaceKind == ProcessingWorkspaceKind.Terminal;

    public Visibility ProcessingWorkspaceVisibility =>
        IsVideoWorkspaceSelected || IsAudioWorkspaceSelected ? Visibility.Visible : Visibility.Collapsed;

    public Visibility TrimWorkspaceVisibility => IsTrimWorkspaceSelected ? Visibility.Visible : Visibility.Collapsed;

    public Visibility MergeWorkspaceVisibility => IsMergeWorkspaceSelected ? Visibility.Visible : Visibility.Collapsed;

    public Visibility SplitAudioWorkspaceVisibility => IsSplitAudioWorkspaceSelected ? Visibility.Visible : Visibility.Collapsed;

    public Visibility TerminalWorkspaceVisibility => IsTerminalWorkspaceSelected ? Visibility.Visible : Visibility.Collapsed;

    public string SettingsPaneDescription =>
        GetLocalizedText(
            "settings.pane.description",
            "应用级偏好统一集中在这里管理，不占用主处理区域；不同模块对处理完成行为和转码方式的生效范围可查看标题旁的说明提示。");

    public Visibility SettingsPaneProcessingBehaviorVisibility =>
        Visibility.Visible;

    public Visibility SettingsPaneTranscodingVisibility =>
        Visibility.Visible;

    public string SettingsPaneProcessingBehaviorInfoTooltip =>
        GetLocalizedText(
            "settings.processingBehavior.infoTooltip",
            "适用范围：" + Environment.NewLine +
            "视频模块、音频模块、裁剪模块、合并模块。" + Environment.NewLine + Environment.NewLine +
            "不适用范围：" + Environment.NewLine +
            "终端模块。" + Environment.NewLine + Environment.NewLine +
            "说明：" + Environment.NewLine +
            "开启后，处理完成时会自动定位输出文件；终端模块不会产出这类处理结果，因此不会使用该设置。");

    public string SettingsPaneTranscodingInfoTooltip =>
        GetLocalizedText(
            "settings.transcoding.infoTooltip",
            "生效范围：" + Environment.NewLine +
            "视频模块的视频转换、视频提取、音频提取；音频模块的音频转换；裁剪模块的视频裁剪、音频裁剪；合并模块的视频拼接、音频拼接、音视频合成。" + Environment.NewLine + Environment.NewLine +
            "不适用范围：" + Environment.NewLine +
            "终端模块；字幕提取不使用快速换封装 / 真正转码 / GPU 加速语义。" + Environment.NewLine + Environment.NewLine +
            "补充说明：" + Environment.NewLine +
            "快速换封装会优先复用可兼容的原始流，遇到拼接、混音、滤镜或格式不兼容时会自动回退为兼容转码。" + Environment.NewLine +
            "真正转码会重新编码输出；GPU 加速仅在真正转码且存在视频编码、输出格式支持硬件 H.264 时生效，音频任务或不支持的格式会继续使用 CPU。");

    public Visibility VideoProcessingModeVisibility => IsVideoWorkspaceSelected ? Visibility.Visible : Visibility.Collapsed;

    public Visibility AudioProcessingModeVisibility => IsAudioWorkspaceSelected ? Visibility.Visible : Visibility.Collapsed;

    public string WorkspaceHeaderCaption =>
        GetLocalizedText("mainWindow.header.caption", "当前模块");

    public string WorkspaceHeaderTitle => GetCurrentWorkspaceProfile().HeaderTitle;

    public string WorkspaceHeaderDescription => GetCurrentWorkspaceProfile().HeaderDescription;

    public string VideoWorkspaceButtonText => GetWorkspaceProfile(ProcessingWorkspaceKind.Video).MediaLabel;

    public string AudioWorkspaceButtonText => GetWorkspaceProfile(ProcessingWorkspaceKind.Audio).MediaLabel;

    public string TrimWorkspaceButtonText => GetWorkspaceProfile(ProcessingWorkspaceKind.Trim).MediaLabel;

    public string MergeWorkspaceButtonText => GetWorkspaceProfile(ProcessingWorkspaceKind.Merge).MediaLabel;

    public string SplitAudioWorkspaceButtonText => GetWorkspaceProfile(ProcessingWorkspaceKind.SplitAudio).MediaLabel;

    public string TerminalWorkspaceButtonText => GetWorkspaceProfile(ProcessingWorkspaceKind.Terminal).MediaLabel;

    public string MainWindowImportFilesButtonLabel =>
        GetLocalizedText("mainWindow.toolbar.importFiles", "导入文件");

    public string MainWindowImportFolderButtonLabel =>
        GetLocalizedText("mainWindow.toolbar.importFolder", "导入文件夹");

    public string MainWindowClearQueueButtonLabel =>
        GetLocalizedText("mainWindow.toolbar.clearQueue", "清空列表");

    public string MainWindowStartProcessingButtonLabel =>
        GetLocalizedText("mainWindow.toolbar.startProcessing", "开始处理");

    public string MainWindowCancelTaskButtonLabel =>
        GetLocalizedText("mainWindow.toolbar.cancelTask", "取消任务");

    public string MainWindowClearCurrentButtonLabel =>
        GetLocalizedText("mainWindow.toolbar.clearCurrent", "清空当前");

    public string MainWindowStartSplitAudioButtonLabel =>
        GetLocalizedText("mainWindow.toolbar.startSplitAudio", "开始拆音");

    public string MainWindowExportTrimSegmentButtonLabel =>
        GetLocalizedText("mainWindow.toolbar.exportTrimSegment", "导出片段");

    public string MainWindowCancelExportButtonLabel =>
        GetLocalizedText("mainWindow.toolbar.cancelExport", "取消导出");

    public string QueueSectionTitleText =>
        GetLocalizedText("mainWindow.queue.title", "待处理队列");

    public string QueueStatusTitleText =>
        GetLocalizedText("mainWindow.queue.statusTitle", "队列状态");

    public string QueueItemDetailsButtonText =>
        GetLocalizedText("mainWindow.queue.item.details", "详情");

    public string QueueItemRemoveButtonText =>
        GetLocalizedText("mainWindow.queue.item.remove", "移除");

    public string ProcessingSettingsTitleText =>
        GetLocalizedText("mainWindow.settings.title", "处理设置");

    public string ProcessingModeLabelText =>
        GetLocalizedText("mainWindow.settings.processingMode", "处理模式");

    public string OutputFormatLabelText =>
        GetLocalizedText("mainWindow.settings.outputFormat", "目标格式");

    public string OutputDirectoryLabelText =>
        GetLocalizedText("mainWindow.settings.outputDirectory", "输出目录");

    public string OutputDirectoryPlaceholderText =>
        GetLocalizedText("mainWindow.settings.outputDirectory.placeholder", "留空时使用原文件夹输出");

    public string SelectOutputDirectoryButtonText =>
        GetLocalizedText("mainWindow.settings.outputDirectory.selectFolder", "选择文件夹");

    public string ClearOutputDirectoryButtonText =>
        GetLocalizedText("mainWindow.settings.outputDirectory.clear", "清空");

    public string OutputDirectoryHintText =>
        GetLocalizedText(
            "mainWindow.settings.outputDirectory.description",
            "留空时使用原文件夹输出；设置后，处理结果会统一输出到所选文件夹。");

    public string ProcessingResultsTitleText =>
        GetLocalizedText("mainWindow.results.title", "处理结果");

    public string QueueDragDropHintText => GetCurrentWorkspaceProfile().QueueDragDropHintText;

    public string DragDropCaptionText => GetCurrentWorkspaceProfile().DragDropCaptionText;

    public string FixedProcessingModeDisplayName => GetWorkspaceProfile(ProcessingWorkspaceKind.Audio).FixedProcessingModeDisplayName;

    public string FixedProcessingModeDescription => GetWorkspaceProfile(ProcessingWorkspaceKind.Audio).FixedProcessingModeDescription;

    private bool IsAudioWorkspace => _selectedWorkspaceKind == ProcessingWorkspaceKind.Audio;

    private bool IsTrimWorkspace => _selectedWorkspaceKind == ProcessingWorkspaceKind.Trim;

    private void RebuildWorkspaceProfiles()
    {
        _workspaceProfiles = _configuration.WorkspaceProfiles
            .ToDictionary(
                pair => pair.Key,
                pair => pair.Value.Localize(_localizationService));
    }

    private void RefreshWorkspaceLocalization()
    {
        OnPropertyChanged(nameof(WorkspaceHeaderTitle));
        OnPropertyChanged(nameof(WorkspaceHeaderDescription));
        OnPropertyChanged(nameof(VideoWorkspaceButtonText));
        OnPropertyChanged(nameof(AudioWorkspaceButtonText));
        OnPropertyChanged(nameof(TrimWorkspaceButtonText));
        OnPropertyChanged(nameof(MergeWorkspaceButtonText));
        OnPropertyChanged(nameof(SplitAudioWorkspaceButtonText));
        OnPropertyChanged(nameof(TerminalWorkspaceButtonText));
        OnPropertyChanged(nameof(MainWindowImportFilesButtonLabel));
        OnPropertyChanged(nameof(MainWindowImportFolderButtonLabel));
        OnPropertyChanged(nameof(MainWindowClearQueueButtonLabel));
        OnPropertyChanged(nameof(MainWindowStartProcessingButtonLabel));
        OnPropertyChanged(nameof(MainWindowCancelTaskButtonLabel));
        OnPropertyChanged(nameof(MainWindowClearCurrentButtonLabel));
        OnPropertyChanged(nameof(MainWindowStartSplitAudioButtonLabel));
        OnPropertyChanged(nameof(MainWindowExportTrimSegmentButtonLabel));
        OnPropertyChanged(nameof(MainWindowCancelExportButtonLabel));
        OnPropertyChanged(nameof(QueueSectionTitleText));
        OnPropertyChanged(nameof(QueueStatusTitleText));
        OnPropertyChanged(nameof(QueueItemDetailsButtonText));
        OnPropertyChanged(nameof(QueueItemRemoveButtonText));
        OnPropertyChanged(nameof(ProcessingSettingsTitleText));
        OnPropertyChanged(nameof(ProcessingModeLabelText));
        OnPropertyChanged(nameof(OutputFormatLabelText));
        OnPropertyChanged(nameof(OutputDirectoryLabelText));
        OnPropertyChanged(nameof(OutputDirectoryPlaceholderText));
        OnPropertyChanged(nameof(SelectOutputDirectoryButtonText));
        OnPropertyChanged(nameof(ClearOutputDirectoryButtonText));
        OnPropertyChanged(nameof(OutputDirectoryHintText));
        OnPropertyChanged(nameof(ProcessingResultsTitleText));
        OnPropertyChanged(nameof(QueueSummaryText));
        OnPropertyChanged(nameof(QueueDragDropHintText));
        OnPropertyChanged(nameof(DragDropCaptionText));
        OnPropertyChanged(nameof(FixedProcessingModeDisplayName));
        OnPropertyChanged(nameof(FixedProcessingModeDescription));
        OnPropertyChanged(nameof(SupportedInputFormatsHint));
        RefreshProcessingImportItemLocalization();
    }

    private ProcessingWorkspaceProfile GetCurrentWorkspaceProfile() =>
        GetWorkspaceProfile(_selectedWorkspaceKind);

    private ProcessingWorkspaceProfile GetWorkspaceProfile(ProcessingWorkspaceKind workspaceKind) =>
        _workspaceProfiles.TryGetValue(workspaceKind, out var profile)
            ? profile
            : _workspaceProfiles.TryGetValue(ProcessingWorkspaceKind.Video, out var fallbackProfile)
                ? fallbackProfile
                : _configuration.WorkspaceProfiles[ProcessingWorkspaceKind.Video].Localize(_localizationService);

    private ObservableCollection<MediaJobViewModel> GetCurrentImportItems() =>
        GetImportItems(_selectedWorkspaceKind);

    private ObservableCollection<MediaJobViewModel> GetImportItems(ProcessingWorkspaceKind workspaceKind) =>
        workspaceKind switch
        {
            ProcessingWorkspaceKind.Audio => _audioImportItems,
            ProcessingWorkspaceKind.Trim => _trimImportItems,
            ProcessingWorkspaceKind.Merge => _mergeImportItems,
            ProcessingWorkspaceKind.SplitAudio => _splitAudioImportItems,
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
            ProcessingWorkspaceKind.SplitAudio => _splitAudioLogEntries,
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

    private Task SwitchToSplitAudioWorkspaceAsync() => SetWorkspaceAsync(ProcessingWorkspaceKind.SplitAudio);

    private Task SwitchToTerminalWorkspaceAsync() => SetWorkspaceAsync(ProcessingWorkspaceKind.Terminal);

    private async Task SetWorkspaceAsync(ProcessingWorkspaceKind workspaceKind)
    {
        if (_selectedWorkspaceKind == workspaceKind)
        {
            return;
        }

        if (IsBusy || TrimWorkspace.IsBusy || MergeWorkspace.IsVideoJoinProcessing || SplitAudioWorkspace.IsBusy)
        {
            StatusMessage = GetLocalizedText("mainWindow.message.workspaceSwitchBusy", "当前任务处理中，暂不支持切换模块。");
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

        if (_selectedWorkspaceKind == ProcessingWorkspaceKind.SplitAudio &&
            workspaceKind != ProcessingWorkspaceKind.SplitAudio)
        {
            try
            {
                await SplitAudioWorkspace.PausePreviewForDeactivationAsync();
            }
            catch (Exception exception)
            {
                _logger.Log(LogLevel.Warning, "Failed to pause split-audio preview while switching workspace.", exception);
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
        OnPropertyChanged(nameof(IsSplitAudioWorkspaceSelected));
        OnPropertyChanged(nameof(IsTerminalWorkspaceSelected));
        OnPropertyChanged(nameof(ProcessingWorkspaceVisibility));
        OnPropertyChanged(nameof(TrimWorkspaceVisibility));
        OnPropertyChanged(nameof(MergeWorkspaceVisibility));
        OnPropertyChanged(nameof(SplitAudioWorkspaceVisibility));
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

    private void RefreshProcessingImportItemLocalization()
    {
        foreach (var item in _videoImportItems)
        {
            item.RefreshLocalization();
        }

        foreach (var item in _audioImportItems)
        {
            item.RefreshLocalization();
        }
    }
}
