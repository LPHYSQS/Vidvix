using Vidvix.Core.Models;

namespace Vidvix.ViewModels;

public sealed partial class MergeViewModel
{
    public string MediaLibrarySectionTitleText =>
        GetLocalizedText("merge.page.mediaLibrary.title", "素材列表");

    public string MediaLibraryDescriptionText =>
        GetLocalizedText(
            "merge.page.mediaLibrary.description",
            "支持导入视频或音频文件。单击素材列表中的素材可添加到相应轨道。");

    public string ImportFilesButtonText =>
        GetLocalizedText("merge.page.mediaLibrary.importButton", "导入文件");

    public string RemoveMediaItemToolTipText =>
        GetLocalizedText("merge.page.action.removeMedia.tooltip", "移除素材");

    public string MediaLibraryEmptyText =>
        GetLocalizedText("merge.page.mediaLibrary.empty", "导入后素材会显示在这里。");

    public string WorkspaceSectionTitleText =>
        GetLocalizedText("merge.page.workspace.title", "合并工作区");

    public string WorkspaceDescriptionText =>
        GetLocalizedText(
            "merge.page.workspace.description",
            "在不同合并模式之间切换，按需将素材编排到视频轨道或音频轨道。");

    public string VideoJoinModeDisplayName => GetModeState(MergeWorkspaceMode.VideoJoin).Profile.DisplayName;

    public string AudioJoinModeDisplayName => GetModeState(MergeWorkspaceMode.AudioJoin).Profile.DisplayName;

    public string AudioVideoComposeModeDisplayName => GetModeState(MergeWorkspaceMode.AudioVideoCompose).Profile.DisplayName;

    public string VideoTrackSectionTitleText =>
        GetLocalizedText("merge.page.track.video.title", "视频轨道");

    public string AudioTrackSectionTitleText =>
        GetLocalizedText("merge.page.track.audio.title", "音频轨道");

    public string VideoTrackOperationHintText =>
        GetLocalizedText(
            "merge.page.track.videoJoinOperationHint",
            "可左键长按拖拽片段调整顺序，也可使用片段顶部按钮快速移除或设为分辨率预设。");

    public string InvalidTrackBadgeText =>
        GetLocalizedText("merge.page.track.invalidBadge", "已失效");

    public string PresetButtonText =>
        GetLocalizedText("merge.page.action.preset.short", "预设");

    public string DurationPresetButtonText =>
        GetLocalizedText("merge.page.action.durationPreset.short", "长度预设");

    public string RemoveButtonText =>
        GetLocalizedText("merge.page.action.remove.short", "移除");

    public string VideoResolutionPresetToolTipText =>
        GetLocalizedText("merge.page.action.setVideoResolutionPreset.tooltip", "设为分辨率预设");

    public string AudioParameterPresetToolTipText =>
        GetLocalizedText("merge.page.action.setAudioParameterPreset.tooltip", "设为参数预设");

    public string AudioVideoComposeVideoPresetBadgeText =>
        GetLocalizedText("merge.page.audioVideoCompose.videoReferenceBadge", "以视频长度为预设");

    public string AudioVideoComposeAudioPresetBadgeText =>
        GetLocalizedText("merge.page.audioVideoCompose.audioReferenceBadge", "以音频长度为预设");

    public string AudioVideoComposeVideoPresetToolTipText =>
        GetLocalizedText("merge.page.action.setVideoDurationPreset.tooltip", "设为视频长度预设");

    public string AudioVideoComposeAudioPresetToolTipText =>
        GetLocalizedText("merge.page.action.setAudioDurationPreset.tooltip", "设为音频长度预设");

    public string RemoveVideoTrackItemToolTipText =>
        GetLocalizedText("merge.page.action.removeVideoSegment.tooltip", "移除视频片段");

    public string RemoveAudioTrackItemToolTipText =>
        GetLocalizedText("merge.page.action.removeAudioSegment.tooltip", "移除音频片段");

    public string AudioVideoComposeCurrentPositionLabelText =>
        GetLocalizedText("merge.page.audioVideoCompose.positionLabel", "当前定位");

    public string AudioVideoComposeVideoStrategyLabelText =>
        GetLocalizedText("merge.page.audioVideoCompose.videoStrategyLabel", "合成策略");

    public string AudioVideoComposeAudioStrategyLabelText =>
        GetLocalizedText("merge.page.audioVideoCompose.audioStrategyLabel", "音轨策略");

    public string AudioVideoComposeVideoExtendModeLabelText =>
        GetLocalizedText("merge.page.audioVideoCompose.videoExtendModeLabel", "视频较短时的延长方式");

    public string AudioVideoComposeExtendLoopOptionText =>
        GetLocalizedText("merge.page.output.option.extendLoop", "循环延长到音频时长");

    public string AudioVideoComposeExtendFreezeOptionText =>
        GetLocalizedText("merge.page.output.option.extendFreeze", "冻结最后一帧到音频结束");

    public string OutputSettingsSectionTitleText =>
        GetLocalizedText("merge.page.output.title", "输出设置");

    public string OutputFormatLabelText =>
        GetLocalizedText("merge.page.output.field.format", "输出格式");

    public string OutputDirectoryLabelText =>
        GetLocalizedText("merge.page.output.field.directory", "输出目录");

    public string OutputFileNameLabelText =>
        GetLocalizedText("merge.page.output.field.fileName", "输出文件名");

    public string ResolutionHandlingLabelText =>
        GetLocalizedText("merge.page.output.field.resolutionHandling", "分辨率处理");

    public string SmallerResolutionVideoLabelText =>
        GetLocalizedText("merge.page.output.field.smallerResolutionVideo", "较小分辨率视频");

    public string LargerResolutionVideoLabelText =>
        GetLocalizedText("merge.page.output.field.largerResolutionVideo", "较大分辨率视频");

    public string ParameterModeLabelText =>
        GetLocalizedText("merge.page.output.field.parameterMode", "参数模式");

    public string ParameterBaselineLabelText =>
        GetLocalizedText("merge.page.output.field.parameterBaseline", "参数基准");

    public string SmartAlignmentStrategyLabelText =>
        GetLocalizedText("merge.page.output.field.smartAlignment", "智能对齐策略");

    public string ImportedAudioProcessingLabelText =>
        GetLocalizedText("merge.page.output.field.importedAudioProcessing", "导入音频处理");

    public string ImportedAudioVolumeLabelText =>
        GetLocalizedText("merge.page.output.field.importedAudioVolume", "导入音频音量");

    public string ImportedAudioProcessingHintText =>
        GetLocalizedText(
            "merge.page.output.description.importedAudioProcessing",
            "导入音频的整体音量在这里统一调整，导出时会按当前分贝直接生效。");

    public string MixingLabelText =>
        GetLocalizedText("merge.page.output.field.mixing", "混音");

    public string MixOriginalAudioHeaderText =>
        GetLocalizedText("merge.page.output.field.mixOriginalAudio", "保留原视频声音并与导入音频混合");

    public string ToggleOffText =>
        GetLocalizedText("common.toggle.off", "关闭");

    public string ToggleOnText =>
        GetLocalizedText("common.toggle.on", "开启");

    public string OriginalVideoAudioVolumeLabelText =>
        GetLocalizedText("merge.page.output.field.originalVideoAudioVolume", "原视频声音音量");

    public string FadeSectionTitleText =>
        GetLocalizedText("merge.page.output.field.fade", "淡入淡出");

    public string FadeInHeaderText =>
        GetLocalizedText("merge.page.output.field.fadeIn", "导入音频淡入");

    public string FadeInDurationHeaderText =>
        GetLocalizedText("merge.page.output.field.fadeInDuration", "淡入时长（秒）");

    public string FadeOutHeaderText =>
        GetLocalizedText("merge.page.output.field.fadeOut", "导入音频淡出");

    public string FadeOutDurationHeaderText =>
        GetLocalizedText("merge.page.output.field.fadeOutDuration", "淡出时长（秒）");

    public string AutoGeneratedOutputFileNamePlaceholderText =>
        GetLocalizedText("merge.page.output.placeholder.autoGeneratedFileName", "留空时自动生成");

    public string VideoJoinOutputDirectoryPlaceholderText =>
        GetLocalizedText("merge.page.output.placeholder.videoPresetDirectory", "预设视频目录");

    public string AudioJoinOutputDirectoryPlaceholderText =>
        GetLocalizedText("merge.page.output.placeholder.audioPresetDirectory", "预设音频目录");

    public string AudioVideoComposeOutputDirectoryPlaceholderText =>
        GetLocalizedText("merge.page.output.placeholder.currentVideoDirectory", "当前视频目录");

    public string SelectOutputDirectoryButtonText =>
        GetLocalizedText("merge.page.output.button.selectDirectory", "选择目录");

    public string ClearOutputDirectoryButtonText =>
        GetLocalizedText("merge.page.output.button.clearDirectory", "清空");

    public string PadWithBlackBarsOptionText =>
        GetLocalizedText("merge.summary.videoResolutionStrategy.smaller.pad", "填充黑边");

    public string StretchToFillOptionText =>
        GetLocalizedText("merge.summary.videoResolutionStrategy.smaller.stretch", "拉伸填充");

    public string SqueezeOptionText =>
        GetLocalizedText("merge.summary.videoResolutionStrategy.larger.squeeze", "挤压");

    public string CropOptionText =>
        GetLocalizedText("merge.summary.videoResolutionStrategy.larger.crop", "裁剪");

    public string BalancedAudioJoinParameterModeText =>
        GetLocalizedText("merge.page.output.option.balancedMode", "均衡模式");

    public string BalancedAudioJoinParameterModeDescriptionText =>
        GetLocalizedText(
            "merge.page.output.description.balancedMode",
            "不锁定单个预设音频，按全部有效音轨的整体情况统一采样率与码率，更偏向稳定听感和输出兼容性。");

    public string PresetAudioJoinParameterModeText =>
        GetLocalizedText("merge.page.output.option.presetMode", "指定预设模式");

    public string PresetAudioJoinParameterModeDescriptionText =>
        GetLocalizedText(
            "merge.page.output.description.presetMode",
            "以音频轨道中选中的参数预设为目标，低于目标的补齐到目标，高于目标的压到目标，匹配的保持不动。");

    public string InvalidTrackDialogCloseButtonText =>
        GetLocalizedText("merge.dialog.invalidTrackItems.closeButton", "知道了");

    public string MediaListDragDropCaptionText =>
        GetLocalizedText("merge.page.dragDrop.caption", "将文件或文件夹拖到这里导入素材");

    private void RaiseUiTextPropertiesChanged()
    {
        OnPropertyChanged(nameof(MediaLibrarySectionTitleText));
        OnPropertyChanged(nameof(MediaLibraryDescriptionText));
        OnPropertyChanged(nameof(ImportFilesButtonText));
        OnPropertyChanged(nameof(RemoveMediaItemToolTipText));
        OnPropertyChanged(nameof(MediaLibraryEmptyText));
        OnPropertyChanged(nameof(WorkspaceSectionTitleText));
        OnPropertyChanged(nameof(WorkspaceDescriptionText));
        OnPropertyChanged(nameof(VideoJoinModeDisplayName));
        OnPropertyChanged(nameof(AudioJoinModeDisplayName));
        OnPropertyChanged(nameof(AudioVideoComposeModeDisplayName));
        OnPropertyChanged(nameof(VideoTrackSectionTitleText));
        OnPropertyChanged(nameof(AudioTrackSectionTitleText));
        OnPropertyChanged(nameof(VideoTrackOperationHintText));
        OnPropertyChanged(nameof(InvalidTrackBadgeText));
        OnPropertyChanged(nameof(PresetButtonText));
        OnPropertyChanged(nameof(DurationPresetButtonText));
        OnPropertyChanged(nameof(RemoveButtonText));
        OnPropertyChanged(nameof(VideoResolutionPresetToolTipText));
        OnPropertyChanged(nameof(AudioParameterPresetToolTipText));
        OnPropertyChanged(nameof(AudioVideoComposeVideoPresetBadgeText));
        OnPropertyChanged(nameof(AudioVideoComposeAudioPresetBadgeText));
        OnPropertyChanged(nameof(AudioVideoComposeVideoPresetToolTipText));
        OnPropertyChanged(nameof(AudioVideoComposeAudioPresetToolTipText));
        OnPropertyChanged(nameof(RemoveVideoTrackItemToolTipText));
        OnPropertyChanged(nameof(RemoveAudioTrackItemToolTipText));
        OnPropertyChanged(nameof(AudioVideoComposeCurrentPositionLabelText));
        OnPropertyChanged(nameof(AudioVideoComposeVideoStrategyLabelText));
        OnPropertyChanged(nameof(AudioVideoComposeAudioStrategyLabelText));
        OnPropertyChanged(nameof(AudioVideoComposeVideoExtendModeLabelText));
        OnPropertyChanged(nameof(AudioVideoComposeExtendLoopOptionText));
        OnPropertyChanged(nameof(AudioVideoComposeExtendFreezeOptionText));
        OnPropertyChanged(nameof(OutputSettingsSectionTitleText));
        OnPropertyChanged(nameof(OutputFormatLabelText));
        OnPropertyChanged(nameof(OutputDirectoryLabelText));
        OnPropertyChanged(nameof(OutputFileNameLabelText));
        OnPropertyChanged(nameof(ResolutionHandlingLabelText));
        OnPropertyChanged(nameof(SmallerResolutionVideoLabelText));
        OnPropertyChanged(nameof(LargerResolutionVideoLabelText));
        OnPropertyChanged(nameof(ParameterModeLabelText));
        OnPropertyChanged(nameof(ParameterBaselineLabelText));
        OnPropertyChanged(nameof(SmartAlignmentStrategyLabelText));
        OnPropertyChanged(nameof(ImportedAudioProcessingLabelText));
        OnPropertyChanged(nameof(ImportedAudioVolumeLabelText));
        OnPropertyChanged(nameof(ImportedAudioProcessingHintText));
        OnPropertyChanged(nameof(MixingLabelText));
        OnPropertyChanged(nameof(MixOriginalAudioHeaderText));
        OnPropertyChanged(nameof(ToggleOffText));
        OnPropertyChanged(nameof(ToggleOnText));
        OnPropertyChanged(nameof(OriginalVideoAudioVolumeLabelText));
        OnPropertyChanged(nameof(FadeSectionTitleText));
        OnPropertyChanged(nameof(FadeInHeaderText));
        OnPropertyChanged(nameof(FadeInDurationHeaderText));
        OnPropertyChanged(nameof(FadeOutHeaderText));
        OnPropertyChanged(nameof(FadeOutDurationHeaderText));
        OnPropertyChanged(nameof(AutoGeneratedOutputFileNamePlaceholderText));
        OnPropertyChanged(nameof(VideoJoinOutputDirectoryPlaceholderText));
        OnPropertyChanged(nameof(AudioJoinOutputDirectoryPlaceholderText));
        OnPropertyChanged(nameof(AudioVideoComposeOutputDirectoryPlaceholderText));
        OnPropertyChanged(nameof(SelectOutputDirectoryButtonText));
        OnPropertyChanged(nameof(ClearOutputDirectoryButtonText));
        OnPropertyChanged(nameof(PadWithBlackBarsOptionText));
        OnPropertyChanged(nameof(StretchToFillOptionText));
        OnPropertyChanged(nameof(SqueezeOptionText));
        OnPropertyChanged(nameof(CropOptionText));
        OnPropertyChanged(nameof(BalancedAudioJoinParameterModeText));
        OnPropertyChanged(nameof(BalancedAudioJoinParameterModeDescriptionText));
        OnPropertyChanged(nameof(PresetAudioJoinParameterModeText));
        OnPropertyChanged(nameof(PresetAudioJoinParameterModeDescriptionText));
        OnPropertyChanged(nameof(InvalidTrackDialogCloseButtonText));
        OnPropertyChanged(nameof(MediaListDragDropCaptionText));
    }
}
