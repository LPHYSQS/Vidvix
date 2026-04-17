using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Vidvix.Core.Interfaces;
using Vidvix.Core.Models;
using Vidvix.Utils;

namespace Vidvix.ViewModels;

public sealed partial class MergeViewModel
{
    private const double AudioVideoComposeDurationToleranceSeconds = 0.5d;
    private const double AudioVideoComposeMinimumDecibels = -36d;
    private const double AudioVideoComposeMaximumDecibels = 12d;

    private readonly IAudioVideoComposeWorkflowService? _audioVideoComposeWorkflowService;
    private OutputFormatOption? _selectedAudioVideoComposeOutputFormat;
    private string _audioVideoComposeOutputDirectory = string.Empty;
    private string _audioVideoComposeOutputFileName = string.Empty;
    private AudioVideoComposeReferenceMode _selectedAudioVideoComposeReferenceMode;
    private AudioVideoComposeVideoExtendMode _selectedAudioVideoComposeVideoExtendMode;
    private double _audioVideoComposeImportedAudioVolumeDecibels;
    private bool _isAudioVideoComposeMixOriginalVideoAudioEnabled;
    private double _audioVideoComposeOriginalVideoVolumeDecibels;
    private bool _isAudioVideoComposeFadeInEnabled;
    private double _audioVideoComposeFadeInSeconds;
    private bool _isAudioVideoComposeFadeOutEnabled;
    private double _audioVideoComposeFadeOutSeconds;

    public IReadOnlyList<OutputFormatOption> AudioVideoComposeOutputFormats => _videoJoinOutputFormats;

    public OutputFormatOption SelectedAudioVideoComposeOutputFormat
    {
        get => _selectedAudioVideoComposeOutputFormat ?? _videoJoinOutputFormats.First();
        set
        {
            if (value is null)
            {
                return;
            }

            if (SetProperty(ref _selectedAudioVideoComposeOutputFormat, value))
            {
                OnPropertyChanged(nameof(SelectedAudioVideoComposeOutputFormatDescription));
                OnPropertyChanged(nameof(AudioVideoComposeResolvedOutputFileName));
                OnPropertyChanged(nameof(AudioVideoComposeOutputNameHintText));
                PersistAudioVideoComposePreferences();
                StatusMessage = $"音视频合成输出格式已切换为 {value.DisplayName}。";
            }
        }
    }

    public string SelectedAudioVideoComposeOutputFormatDescription => SelectedAudioVideoComposeOutputFormat.Description;

    public string AudioVideoComposeOutputDirectory
    {
        get => _audioVideoComposeOutputDirectory;
        set
        {
            var normalizedDirectory = NormalizeOutputDirectory(value);
            if (SetProperty(ref _audioVideoComposeOutputDirectory, normalizedDirectory))
            {
                OnPropertyChanged(nameof(AudioVideoComposeHasCustomOutputDirectory));
                OnPropertyChanged(nameof(AudioVideoComposeOutputDirectoryDisplayText));
                PersistAudioVideoComposePreferences();
                NotifyCommandStates();
            }
        }
    }

    public bool AudioVideoComposeHasCustomOutputDirectory => !string.IsNullOrWhiteSpace(AudioVideoComposeOutputDirectory);

    public string AudioVideoComposeOutputDirectoryDisplayText =>
        AudioVideoComposeHasCustomOutputDirectory
            ? AudioVideoComposeOutputDirectory
            : GetDefaultAudioVideoComposeOutputDirectory() ?? string.Empty;

    public string AudioVideoComposeOutputDirectoryHintText =>
        "默认跟随当前视频素材所在文件夹；设置后，音视频合成结果会统一输出到所选目录。";

    public string AudioVideoComposeOutputFileName
    {
        get => _audioVideoComposeOutputFileName;
        set
        {
            var normalizedValue = NormalizeOutputFileName(value);
            if (SetProperty(ref _audioVideoComposeOutputFileName, normalizedValue))
            {
                OnPropertyChanged(nameof(AudioVideoComposeResolvedOutputFileName));
                OnPropertyChanged(nameof(AudioVideoComposeOutputNameHintText));
                PersistAudioVideoComposePreferences();
            }
        }
    }

    public string AudioVideoComposeResolvedOutputFileName =>
        $"{GetEffectiveAudioVideoComposeOutputBaseName()}{SelectedAudioVideoComposeOutputFormat.Extension}";

    public string AudioVideoComposeOutputNameHintText =>
        $"{(string.IsNullOrWhiteSpace(AudioVideoComposeOutputFileName) ? "留空时默认使用" : "当前将输出为")} {AudioVideoComposeResolvedOutputFileName}；若目标目录中已存在同名文件，系统会自动追加序号，避免覆盖原始文件。";

    public Visibility AudioVideoComposeOutputSettingsVisibility =>
        IsAudioVideoComposeModeSelected ? Visibility.Visible : Visibility.Collapsed;

    public bool IsAudioVideoComposeVideoReferenceSelected
    {
        get => _selectedAudioVideoComposeReferenceMode == AudioVideoComposeReferenceMode.Video;
        set
        {
            if (value)
            {
                ApplyAudioVideoComposeReferenceModeSelection(AudioVideoComposeReferenceMode.Video);
            }
        }
    }

    public bool IsAudioVideoComposeAudioReferenceSelected
    {
        get => _selectedAudioVideoComposeReferenceMode == AudioVideoComposeReferenceMode.Audio;
        set
        {
            if (value)
            {
                ApplyAudioVideoComposeReferenceModeSelection(AudioVideoComposeReferenceMode.Audio);
            }
        }
    }

    public bool IsAudioVideoComposeLoopVideoExtendModeSelected
    {
        get => _selectedAudioVideoComposeVideoExtendMode == AudioVideoComposeVideoExtendMode.Loop;
        set
        {
            if (value)
            {
                SetAudioVideoComposeVideoExtendMode(AudioVideoComposeVideoExtendMode.Loop);
            }
        }
    }

    public bool IsAudioVideoComposeFreezeFrameVideoExtendModeSelected
    {
        get => _selectedAudioVideoComposeVideoExtendMode == AudioVideoComposeVideoExtendMode.FreezeLastFrame;
        set
        {
            if (value)
            {
                SetAudioVideoComposeVideoExtendMode(AudioVideoComposeVideoExtendMode.FreezeLastFrame);
            }
        }
    }

    public double AudioVideoComposeImportedAudioVolumeDecibels
    {
        get => _audioVideoComposeImportedAudioVolumeDecibels;
        set
        {
            var normalizedValue = NormalizeAudioVideoComposeDecibels(value);
            if (SetProperty(ref _audioVideoComposeImportedAudioVolumeDecibels, normalizedValue))
            {
                OnPropertyChanged(nameof(AudioVideoComposeImportedAudioVolumeText));
                PersistAudioVideoComposePreferences();
            }
        }
    }

    public string AudioVideoComposeImportedAudioVolumeText =>
        FormatAudioVideoComposeDecibelText(_audioVideoComposeImportedAudioVolumeDecibels);

    public bool IsAudioVideoComposeMixOriginalAudioEnabled
    {
        get => _isAudioVideoComposeMixOriginalVideoAudioEnabled && AudioVideoComposeCanMixOriginalAudio;
        set
        {
            if (value && !AudioVideoComposeCanMixOriginalAudio)
            {
                StatusMessage = "当前暂无可用于混音的音频来源，请先添加带声音的视频或音频素材。";
                OnPropertyChanged(nameof(IsAudioVideoComposeMixOriginalAudioEnabled));
                return;
            }

            if (SetProperty(ref _isAudioVideoComposeMixOriginalVideoAudioEnabled, value))
            {
                OnPropertyChanged(nameof(AudioVideoComposeMixOriginalAudioAvailabilityText));
                OnPropertyChanged(nameof(AudioVideoComposeOriginalAudioControlsVisibility));
                PersistAudioVideoComposePreferences();
                StatusMessage = value
                    ? GetAudioVideoComposeVideoHasEmbeddedAudio()
                        ? "已开启原视频声音混音。"
                        : "已开启混音处理；当前导出将继续以导入音频为主。"
                    : "已关闭原视频声音混音，导出时仅保留导入音频。";
            }
        }
    }

    public bool AudioVideoComposeCanMixOriginalAudio => HasAudioVideoComposeAnyAudioSource();

    public string AudioVideoComposeMixOriginalAudioAvailabilityText
    {
        get
        {
            if (!HasAudioVideoComposeAnyTrackItem())
            {
                return "添加视频或音频素材后，即可根据当前可用音频源开启混音。";
            }

            if (GetAudioVideoComposeAudioTrackItem() is not null &&
                GetAudioVideoComposeVideoHasEmbeddedAudio())
            {
                return "当前已检测到导入音频与视频原声，可按需开启混音并分别调整两路音量。";
            }

            if (GetAudioVideoComposeAudioTrackItem() is not null)
            {
                return "当前已有导入音频；若源视频不含原声，导出时将仅保留导入音频。";
            }

            return "当前视频检测到可用原声；添加导入音频后可与原声一起参与混音。";
        }
    }

    public Visibility AudioVideoComposeOriginalAudioControlsVisibility =>
        IsAudioVideoComposeMixOriginalAudioEnabled && GetAudioVideoComposeVideoHasEmbeddedAudio()
            ? Visibility.Visible
            : Visibility.Collapsed;

    public double AudioVideoComposeOriginalVideoVolumeDecibels
    {
        get => _audioVideoComposeOriginalVideoVolumeDecibels;
        set
        {
            var normalizedValue = NormalizeAudioVideoComposeDecibels(value);
            if (SetProperty(ref _audioVideoComposeOriginalVideoVolumeDecibels, normalizedValue))
            {
                OnPropertyChanged(nameof(AudioVideoComposeOriginalVideoVolumeText));
                PersistAudioVideoComposePreferences();
            }
        }
    }

    public string AudioVideoComposeOriginalVideoVolumeText =>
        FormatAudioVideoComposeDecibelText(_audioVideoComposeOriginalVideoVolumeDecibels);

    public bool IsAudioVideoComposeFadeInEnabled
    {
        get => _isAudioVideoComposeFadeInEnabled;
        set
        {
            if (SetProperty(ref _isAudioVideoComposeFadeInEnabled, value))
            {
                PersistAudioVideoComposePreferences();
                StatusMessage = value ? "已启用导入音频淡入。" : "已关闭导入音频淡入。";
            }
        }
    }

    public double AudioVideoComposeFadeInSeconds
    {
        get => _audioVideoComposeFadeInSeconds;
        set
        {
            var normalizedValue = NormalizeAudioVideoComposeFadeSeconds(value);
            if (SetProperty(ref _audioVideoComposeFadeInSeconds, normalizedValue))
            {
                OnPropertyChanged(nameof(AudioVideoComposeFadeHintText));
                PersistAudioVideoComposePreferences();
            }
        }
    }

    public bool IsAudioVideoComposeFadeOutEnabled
    {
        get => _isAudioVideoComposeFadeOutEnabled;
        set
        {
            if (SetProperty(ref _isAudioVideoComposeFadeOutEnabled, value))
            {
                PersistAudioVideoComposePreferences();
                StatusMessage = value ? "已启用导入音频淡出。" : "已关闭导入音频淡出。";
            }
        }
    }

    public double AudioVideoComposeFadeOutSeconds
    {
        get => _audioVideoComposeFadeOutSeconds;
        set
        {
            var normalizedValue = NormalizeAudioVideoComposeFadeSeconds(value);
            if (SetProperty(ref _audioVideoComposeFadeOutSeconds, normalizedValue))
            {
                OnPropertyChanged(nameof(AudioVideoComposeFadeHintText));
                PersistAudioVideoComposePreferences();
            }
        }
    }

    public string AudioVideoComposeFadeHintText
    {
        get
        {
            var targetDuration = GetAudioVideoComposeTargetDuration();
            if (targetDuration is not { } duration || duration <= TimeSpan.Zero)
            {
                return "添加完整的 1 个视频和 1 个音频后，淡入和淡出秒数会自动限制在目标输出时长之内。";
            }

            return $"当前目标输出时长为 {FormatDuration(duration)}；淡入和淡出秒数都会自动限制在这个范围内。";
        }
    }

    public string AudioVideoComposeDurationSummaryText
    {
        get
        {
            if (!TryGetAudioVideoComposeDurations(out var videoDuration, out var audioDuration))
            {
                return "请在音视频合成模式下分别放入 1 个视频和 1 个音频。再次添加同类型素材时，会自动替换当前轨道内容。";
            }

            return HasAudioVideoComposeDurationMismatch()
                ? $"当前视频 {FormatDuration(videoDuration)}，音频 {FormatDuration(audioDuration)}，请选择以哪一轨作为长度预设。"
                : $"当前视频 {FormatDuration(videoDuration)}，音频 {FormatDuration(audioDuration)}，两者时长已自然对齐。";
        }
    }

    public string AudioVideoComposeStrategySummaryText
    {
        get
        {
            if (!TryGetAudioVideoComposeDurations(out var videoDuration, out var audioDuration))
            {
                return "音视频合成模式不提供精细时间轴，只围绕单视频 + 单音频做智能对齐和导出。";
            }

            if (!HasAudioVideoComposeDurationMismatch())
            {
                return "当前无需额外裁剪或补齐，导出时会直接使用原视频画面并对齐导入音频。";
            }

            if (GetEffectiveAudioVideoComposeReferenceMode() == AudioVideoComposeReferenceMode.Video)
            {
                return audioDuration >= videoDuration
                    ? "当前以视频为准：导入音频会自动裁剪到视频长度。"
                    : "当前以视频为准：导入音频会循环补齐到视频长度，最后一轮仅保留满足时长的部分。";
            }

            if (videoDuration >= audioDuration)
            {
                return "当前以音频为准：视频会自动裁剪到音频长度。";
            }

            return _selectedAudioVideoComposeVideoExtendMode == AudioVideoComposeVideoExtendMode.Loop
                ? "当前以音频为准：视频会循环延长到音频长度。"
                : "当前以音频为准：视频会冻结最后一帧延长到音频长度。";
        }
    }

    public TrackItem? AudioVideoComposeVideoTrackItem => GetAudioVideoComposeVideoTrackItem();

    public TrackItem? AudioVideoComposeAudioTrackItem => GetAudioVideoComposeAudioTrackItem();

    public Visibility AudioVideoComposeVideoTrackItemVisibility =>
        AudioVideoComposeVideoTrackItem is null ? Visibility.Collapsed : Visibility.Visible;

    public Visibility AudioVideoComposeAudioTrackItemVisibility =>
        AudioVideoComposeAudioTrackItem is null ? Visibility.Collapsed : Visibility.Visible;

    public string AudioVideoComposeVideoTrackSummaryText
    {
        get
        {
            if (GetAudioVideoComposeVideoTrackItem() is null)
            {
                return "从素材列表添加 1 个视频后，这里会负责画面输出和原视频声音混音。";
            }

            return GetAudioVideoComposeVideoHasEmbeddedAudio()
                ? "当前视频包含可用原声，可按需与导入音频混合输出。"
                : "当前视频不包含可用原声，导出时会直接使用导入音频。";
        }
    }

    public string AudioVideoComposeAudioTrackSummaryText =>
        GetAudioVideoComposeAudioTrackItem() is null
            ? "从素材列表添加 1 个音频后，这里会负责配乐、解说等导入音轨处理。"
            : "导入音频可在右侧统一调整音量、淡入、淡出和混音策略。";

    public string AudioVideoComposeVideoDurationText =>
        GetAudioVideoComposeVideoTrackItem() is not null &&
        TryResolveAudioVideoComposeTrackDuration(GetAudioVideoComposeVideoTrackItem(), out var duration)
            ? $"原始时长 · {FormatDuration(duration)}"
            : "原始时长 · --:--:--";

    public string AudioVideoComposeAudioDurationText =>
        GetAudioVideoComposeAudioTrackItem() is not null &&
        TryResolveAudioVideoComposeTrackDuration(GetAudioVideoComposeAudioTrackItem(), out var duration)
            ? $"原始时长 · {FormatDuration(duration)}"
            : "原始时长 · --:--:--";

    public Visibility AudioVideoComposePresetSelectionVisibility =>
        HasAudioVideoComposeAnyTrackItem() ? Visibility.Visible : Visibility.Collapsed;

    public Visibility AudioVideoComposeVideoExtendOptionsVisibility =>
        IsAudioVideoComposeModeSelected &&
        GetEffectiveAudioVideoComposeReferenceMode() == AudioVideoComposeReferenceMode.Audio &&
        IsAudioVideoComposeVideoShorterThanAudio()
            ? Visibility.Visible
            : Visibility.Collapsed;

    public string AudioVideoComposeVideoCardStrategyText
    {
        get
        {
            if (GetAudioVideoComposeVideoTrackItem() is null)
            {
                return "视频轨道只保留 1 个素材，再次添加视频时会自动替换当前视频。";
            }

            if (!TryGetAudioVideoComposeDurations(out var videoDuration, out var audioDuration))
            {
                return "当前视频将作为唯一画面源参与输出。";
            }

            if (!HasAudioVideoComposeDurationMismatch())
            {
                return "当前视频时长与音频一致，无需额外延长或裁剪。";
            }

            if (GetEffectiveAudioVideoComposeReferenceMode() == AudioVideoComposeReferenceMode.Video)
            {
                return "当前视频被设为长度预设，画面会完整保留到导出结束。";
            }

            return videoDuration >= audioDuration
                ? "当前视频长于音频，导出时会按音频长度自动裁剪视频。"
                : _selectedAudioVideoComposeVideoExtendMode == AudioVideoComposeVideoExtendMode.Loop
                    ? "当前视频短于音频，导出时会循环延长视频直到满足音频时长。"
                    : "当前视频短于音频，导出时会冻结最后一帧直到满足音频时长。";
        }
    }

    public string AudioVideoComposeAudioCardStrategyText
    {
        get
        {
            if (GetAudioVideoComposeAudioTrackItem() is null)
            {
                return "音频轨道只保留 1 个素材，再次添加音频时会自动替换当前音频。";
            }

            if (!TryGetAudioVideoComposeDurations(out var videoDuration, out var audioDuration))
            {
                return "当前导入音频会作为主要输出音轨参与合成。";
            }

            if (!HasAudioVideoComposeDurationMismatch())
            {
                return "当前音频时长与视频一致，可直接按原始长度参与输出。";
            }

            if (GetEffectiveAudioVideoComposeReferenceMode() == AudioVideoComposeReferenceMode.Audio)
            {
                return "当前音频被设为长度预设，导出时会完整保留到结束。";
            }

            return audioDuration >= videoDuration
                ? "当前音频长于视频，导出时会按视频长度自动裁剪导入音频。"
                : "当前音频短于视频，导出时会循环补齐到视频长度，最后一轮仅保留满足时长的部分。";
        }
    }

    private void InitializeAudioVideoComposeState(UserPreferences preferences)
    {
        _selectedAudioVideoComposeOutputFormat =
            ResolvePreferredVideoJoinOutputFormat(preferences.PreferredMergeAudioVideoComposeOutputFormatExtension);
        _audioVideoComposeOutputDirectory = NormalizeOutputDirectory(preferences.PreferredMergeAudioVideoComposeOutputDirectory);
        _audioVideoComposeOutputFileName = string.Empty;
        _selectedAudioVideoComposeReferenceMode =
            ResolvePreferredAudioVideoComposeReferenceMode(preferences.PreferredMergeAudioVideoComposeReferenceMode);
        _selectedAudioVideoComposeVideoExtendMode =
            ResolvePreferredAudioVideoComposeVideoExtendMode(preferences.PreferredMergeAudioVideoComposeVideoExtendMode);
        _audioVideoComposeImportedAudioVolumeDecibels =
            NormalizeAudioVideoComposeDecibels(preferences.PreferredMergeAudioVideoComposeImportedAudioVolumeDecibels);
        _isAudioVideoComposeMixOriginalVideoAudioEnabled = preferences.PreferredMergeAudioVideoComposeMixOriginalVideoAudio;
        _audioVideoComposeOriginalVideoVolumeDecibels =
            NormalizeAudioVideoComposeDecibels(preferences.PreferredMergeAudioVideoComposeOriginalVideoVolumeDecibels);
        _isAudioVideoComposeFadeInEnabled = preferences.PreferredMergeAudioVideoComposeEnableFadeIn;
        _audioVideoComposeFadeInSeconds = NormalizeAudioVideoComposeFadeSeconds(preferences.PreferredMergeAudioVideoComposeFadeInSeconds);
        _isAudioVideoComposeFadeOutEnabled = preferences.PreferredMergeAudioVideoComposeEnableFadeOut;
        _audioVideoComposeFadeOutSeconds = NormalizeAudioVideoComposeFadeSeconds(preferences.PreferredMergeAudioVideoComposeFadeOutSeconds);
    }

    private async Task StartAudioVideoComposeProcessingAsync()
    {
        if (!CanStartAudioVideoComposeProcessing())
        {
            StatusMessage = GetAudioVideoComposeCannotStartMessage();
            return;
        }

        if (_audioVideoComposeWorkflowService is null || _mergeMediaAnalysisService is null)
        {
            StatusMessage = "当前环境暂不支持音视频合成输出。";
            return;
        }

        var videoTrackItem = GetAudioVideoComposeVideoTrackItem()
                             ?? throw new InvalidOperationException("音视频合成缺少视频素材。");
        var audioTrackItem = GetAudioVideoComposeAudioTrackItem()
                             ?? throw new InvalidOperationException("音视频合成缺少音频素材。");

        if (!IsAudioVideoComposeTrackProcessable(videoTrackItem))
        {
            StatusMessage = "当前视频轨道素材不可用，请移除后重新添加。";
            return;
        }

        if (!IsAudioVideoComposeTrackProcessable(audioTrackItem))
        {
            StatusMessage = "当前音频轨道素材不可用，请移除后重新添加。";
            return;
        }

        _videoJoinProcessingCancellationSource?.Dispose();
        _videoJoinProcessingCancellationSource = new CancellationTokenSource();
        var cancellationToken = _videoJoinProcessingCancellationSource.Token;

        try
        {
            IsVideoJoinProcessing = true;
            ShowProcessingPreparationProgress("音视频合成", "正在检查音视频素材并准备合成参数...");
            StatusMessage = "正在检查音视频素材并准备合成参数...";

            EnsureAudioVideoComposeOutputDirectoryExists();

            var sourceAnalysis = await _mergeMediaAnalysisService.AnalyzeAudioVideoComposeAsync(
                videoTrackItem,
                audioTrackItem,
                cancellationToken);
            var request = BuildAudioVideoComposeExportRequest(
                videoTrackItem,
                audioTrackItem,
                sourceAnalysis);

            StatusMessage = "正在准备 FFmpeg 运行时...";
            await _audioVideoComposeWorkflowService.EnsureRuntimeReadyAsync(cancellationToken);

            StatusMessage = $"FFmpeg 已就绪，正在合成音视频：{FormatDuration(request.OutputDuration)}";
            var progressReporter = new Progress<FFmpegProgressUpdate>(HandleAudioVideoComposeProgress);
            var exportResult = await _audioVideoComposeWorkflowService.ExportAsync(
                request,
                progressReporter,
                () => StatusMessage = "GPU 编码失败，已自动回退为 CPU 重试一次。",
                cancellationToken);
            var result = exportResult.ExecutionResult;

            if (result.WasSuccessful && File.Exists(exportResult.Request.OutputPath))
            {
                StatusMessage = AppendTranscodingMessage(
                    BuildAudioVideoComposeCompletionMessage(exportResult.Request),
                    exportResult.TranscodingMessage);
                TryRevealVideoJoinOutputFile(exportResult.Request.OutputPath);
                return;
            }

            StatusMessage = result.WasCancelled
                ? "已取消音视频合成任务。"
                : AppendTranscodingMessage(
                    $"音视频合成失败：{ExtractFriendlyVideoJoinFailureMessage(result)}",
                    exportResult.TranscodingMessage);
        }
        catch (OperationCanceledException) when (_videoJoinProcessingCancellationSource?.IsCancellationRequested == true)
        {
            StatusMessage = "已取消音视频合成任务。";
        }
        catch (Exception exception)
        {
            StatusMessage = $"音视频合成任务执行失败：{exception.Message}";
            _logger?.Log(LogLevel.Error, "执行音视频合成任务时发生异常。", exception);
        }
        finally
        {
            ResetProcessingProgress();
            IsVideoJoinProcessing = false;
            _videoJoinProcessingCancellationSource?.Dispose();
            _videoJoinProcessingCancellationSource = null;
        }
    }

    private AudioVideoComposeExportRequest BuildAudioVideoComposeExportRequest(
        TrackItem videoTrackItem,
        TrackItem audioTrackItem,
        AudioVideoComposeSourceAnalysis sourceAnalysis)
    {
        var preferences = GetCurrentUserPreferences();
        var plannedOutputPath = CreatePlannedAudioVideoComposeOutputPath(videoTrackItem.SourcePath);
        var targetDuration = GetAudioVideoComposeTargetDuration() ?? sourceAnalysis.VideoDuration;
        var fadeInDuration = _isAudioVideoComposeFadeInEnabled
            ? TimeSpan.FromSeconds(Math.Min(_audioVideoComposeFadeInSeconds, targetDuration.TotalSeconds))
            : TimeSpan.Zero;
        var fadeOutDuration = _isAudioVideoComposeFadeOutEnabled
            ? TimeSpan.FromSeconds(Math.Min(_audioVideoComposeFadeOutSeconds, targetDuration.TotalSeconds))
            : TimeSpan.Zero;

        return new AudioVideoComposeExportRequest(
            videoTrackItem.SourcePath,
            videoTrackItem.SourceName,
            sourceAnalysis.VideoDuration,
            sourceAnalysis.VideoFrameRate,
            sourceAnalysis.VideoHasAudioStream,
            sourceAnalysis.VideoCodecName,
            sourceAnalysis.VideoContainerExtension,
            audioTrackItem.SourcePath,
            audioTrackItem.SourceName,
            sourceAnalysis.AudioDuration,
            sourceAnalysis.AudioCodecName,
            sourceAnalysis.AudioContainerExtension,
            plannedOutputPath,
            SelectedAudioVideoComposeOutputFormat,
            preferences.PreferredTranscodingMode,
            preferences.EnableGpuAccelerationForTranscoding,
            VideoAccelerationKind.None,
            GetEffectiveAudioVideoComposeReferenceMode(),
            _selectedAudioVideoComposeVideoExtendMode,
            _audioVideoComposeImportedAudioVolumeDecibels,
            _isAudioVideoComposeMixOriginalVideoAudioEnabled && sourceAnalysis.VideoHasAudioStream,
            _audioVideoComposeOriginalVideoVolumeDecibels,
            _isAudioVideoComposeFadeInEnabled,
            fadeInDuration,
            _isAudioVideoComposeFadeOutEnabled,
            fadeOutDuration);
    }

    private bool CanStartAudioVideoComposeProcessing() =>
        _audioVideoComposeWorkflowService is not null &&
        !IsVideoJoinProcessing &&
        _selectedMergeMode == MergeWorkspaceMode.AudioVideoCompose &&
        GetAudioVideoComposeVideoTrackItem() is not null &&
        GetAudioVideoComposeAudioTrackItem() is not null;

    private string GetAudioVideoComposeCannotStartMessage()
    {
        if (_selectedMergeMode != MergeWorkspaceMode.AudioVideoCompose)
        {
            return "当前开始处理仅适用于音视频合成模式。";
        }

        if (IsVideoJoinProcessing)
        {
            return "当前合并任务正在进行中。";
        }

        if (_audioVideoComposeWorkflowService is null)
        {
            return "当前环境暂不支持音视频合成输出。";
        }

        if (GetAudioVideoComposeVideoTrackItem() is null)
        {
            return "请先向视频轨道添加 1 个有效视频。";
        }

        if (GetAudioVideoComposeAudioTrackItem() is null)
        {
            return "请先向音频轨道添加 1 个有效音频。";
        }

        return "当前音视频合成缺少可处理的素材。";
    }

    public void SetAudioVideoComposeVideoPreset(TrackItem trackItem)
    {
        ArgumentNullException.ThrowIfNull(trackItem);

        if (IsVideoJoinProcessing)
        {
            StatusMessage = "当前合并任务处理中，若需切换长度预设，请先取消当前任务。";
            return;
        }

        if (!_audioVideoComposeVideoTrackItems.Contains(trackItem))
        {
            return;
        }

        if (!trackItem.CanSetAsResolutionPreset)
        {
            StatusMessage = "当前视频源素材已失效，无法设为长度预设。";
            return;
        }

        _selectedAudioVideoComposeReferenceMode = AudioVideoComposeReferenceMode.Video;
        OnPropertyChanged(nameof(IsAudioVideoComposeVideoReferenceSelected));
        OnPropertyChanged(nameof(IsAudioVideoComposeAudioReferenceSelected));
        RefreshTrackCollection(_audioVideoComposeVideoTrackItems, supportsVideoPreset: false);
        RefreshTrackCollection(_audioVideoComposeAudioTrackItems, supportsVideoPreset: false);
        RaiseAudioVideoComposeStatePropertiesChanged();
        PersistAudioVideoComposePreferences();
        StatusMessage = GetAudioVideoComposeAudioTrackItem() is not null && HasAudioVideoComposeDurationMismatch()
            ? "已切换为以视频为准，导入音频会自动匹配到视频长度。"
            : "已将视频设为当前长度预设。";
    }

    public void SetAudioVideoComposeAudioPreset(TrackItem trackItem)
    {
        ArgumentNullException.ThrowIfNull(trackItem);

        if (IsVideoJoinProcessing)
        {
            StatusMessage = "当前合并任务处理中，若需切换长度预设，请先取消当前任务。";
            return;
        }

        if (!_audioVideoComposeAudioTrackItems.Contains(trackItem))
        {
            return;
        }

        if (!trackItem.CanSetAsResolutionPreset)
        {
            StatusMessage = "当前音频源素材已失效，无法设为长度预设。";
            return;
        }

        _selectedAudioVideoComposeReferenceMode = AudioVideoComposeReferenceMode.Audio;
        OnPropertyChanged(nameof(IsAudioVideoComposeVideoReferenceSelected));
        OnPropertyChanged(nameof(IsAudioVideoComposeAudioReferenceSelected));
        RefreshTrackCollection(_audioVideoComposeVideoTrackItems, supportsVideoPreset: false);
        RefreshTrackCollection(_audioVideoComposeAudioTrackItems, supportsVideoPreset: false);
        RaiseAudioVideoComposeStatePropertiesChanged();
        PersistAudioVideoComposePreferences();
        StatusMessage = GetAudioVideoComposeVideoTrackItem() is not null && HasAudioVideoComposeDurationMismatch()
            ? "已切换为以音频为准，视频会按当前策略自动匹配到音频长度。"
            : "已将音频设为当前长度预设。";
    }

    private void AddMediaToAudioVideoComposeTimeline(MediaItem mediaItem, ObservableCollection<TrackItem> trackItems)
    {
        ArgumentNullException.ThrowIfNull(mediaItem);
        ArgumentNullException.ThrowIfNull(trackItems);

        var isReplacement = trackItems.Count > 0;
        trackItems.Clear();
        trackItems.Add(CreateTrackItem(mediaItem, 1, IsSourcePathAvailable(mediaItem.SourcePath)));

        StatusMessage = isReplacement
            ? $"{mediaItem.FileName} 已替换当前{(mediaItem.IsVideo ? "视频" : "音频")}轨道素材。"
            : $"{mediaItem.FileName} 已加入{(mediaItem.IsVideo ? "视频" : "音频")}轨道。";
    }

    private void SetAudioVideoComposeReferenceMode(AudioVideoComposeReferenceMode referenceMode)
    {
        if (_selectedAudioVideoComposeReferenceMode == referenceMode)
        {
            return;
        }

        if (IsVideoJoinProcessing)
        {
            StatusMessage = "当前合并任务处理中，若需切换长度预设，请先取消当前任务。";
            OnPropertyChanged(nameof(IsAudioVideoComposeVideoReferenceSelected));
            OnPropertyChanged(nameof(IsAudioVideoComposeAudioReferenceSelected));
            return;
        }

        if (!HasAudioVideoComposeAnyTrackItem())
        {
            StatusMessage = "请先在音视频合成轨道中添加素材，再切换预设参考。";
            OnPropertyChanged(nameof(IsAudioVideoComposeVideoReferenceSelected));
            OnPropertyChanged(nameof(IsAudioVideoComposeAudioReferenceSelected));
            return;
        }

        _selectedAudioVideoComposeReferenceMode = referenceMode;
        OnPropertyChanged(nameof(IsAudioVideoComposeVideoReferenceSelected));
        OnPropertyChanged(nameof(IsAudioVideoComposeAudioReferenceSelected));
        RefreshTrackCollection(_audioVideoComposeVideoTrackItems, supportsVideoPreset: false);
        RefreshTrackCollection(_audioVideoComposeAudioTrackItems, supportsVideoPreset: false);
        RaiseAudioVideoComposeStatePropertiesChanged();
        PersistAudioVideoComposePreferences();
        StatusMessage = referenceMode == AudioVideoComposeReferenceMode.Video
            ? "已切换为以视频为准。"
            : "已切换为以音频为准。";
    }

    private void ApplyAudioVideoComposeReferenceModeSelection(AudioVideoComposeReferenceMode referenceMode)
    {
        if (_selectedAudioVideoComposeReferenceMode == referenceMode)
        {
            return;
        }

        if (IsVideoJoinProcessing)
        {
            StatusMessage = "当前合并任务处理中，若需切换长度预设，请先取消当前任务。";
            OnPropertyChanged(nameof(IsAudioVideoComposeVideoReferenceSelected));
            OnPropertyChanged(nameof(IsAudioVideoComposeAudioReferenceSelected));
            return;
        }

        if (!HasAudioVideoComposeAnyTrackItem())
        {
            StatusMessage = "请先在音视频合成轨道中添加素材，再选择预设参考。";
            OnPropertyChanged(nameof(IsAudioVideoComposeVideoReferenceSelected));
            OnPropertyChanged(nameof(IsAudioVideoComposeAudioReferenceSelected));
            return;
        }

        _selectedAudioVideoComposeReferenceMode = referenceMode;
        OnPropertyChanged(nameof(IsAudioVideoComposeVideoReferenceSelected));
        OnPropertyChanged(nameof(IsAudioVideoComposeAudioReferenceSelected));
        RefreshTrackCollection(_audioVideoComposeVideoTrackItems, supportsVideoPreset: false);
        RefreshTrackCollection(_audioVideoComposeAudioTrackItems, supportsVideoPreset: false);
        RaiseAudioVideoComposeStatePropertiesChanged();
        PersistAudioVideoComposePreferences();
        StatusMessage = referenceMode switch
        {
            AudioVideoComposeReferenceMode.Video when HasAudioVideoComposeDurationMismatch() =>
                "已切换为以视频为准，导入音频会自动匹配到视频长度。",
            AudioVideoComposeReferenceMode.Audio when HasAudioVideoComposeDurationMismatch() =>
                "已切换为以音频为准，视频会按当前策略自动匹配到音频长度。",
            AudioVideoComposeReferenceMode.Video =>
                "已将视频设为当前长度预设。",
            _ =>
                "已将音频设为当前长度预设。"
        };
    }

    private void SetAudioVideoComposeVideoExtendMode(AudioVideoComposeVideoExtendMode videoExtendMode)
    {
        if (_selectedAudioVideoComposeVideoExtendMode == videoExtendMode)
        {
            return;
        }

        if (IsVideoJoinProcessing)
        {
            StatusMessage = "当前合并任务处理中，若需切换延长策略，请先取消当前任务。";
            OnPropertyChanged(nameof(IsAudioVideoComposeLoopVideoExtendModeSelected));
            OnPropertyChanged(nameof(IsAudioVideoComposeFreezeFrameVideoExtendModeSelected));
            return;
        }

        _selectedAudioVideoComposeVideoExtendMode = videoExtendMode;
        OnPropertyChanged(nameof(IsAudioVideoComposeLoopVideoExtendModeSelected));
        OnPropertyChanged(nameof(IsAudioVideoComposeFreezeFrameVideoExtendModeSelected));
        RaiseAudioVideoComposeStatePropertiesChanged();
        PersistAudioVideoComposePreferences();

        if (AudioVideoComposeVideoExtendOptionsVisibility == Visibility.Visible)
        {
            StatusMessage = videoExtendMode == AudioVideoComposeVideoExtendMode.Loop
                ? "当前视频较短时，将通过循环延长到音频长度。"
                : "当前视频较短时，将冻结最后一帧延长到音频长度。";
        }
    }

    private void PersistAudioVideoComposePreferences()
    {
        if (_userPreferencesService is null)
        {
            return;
        }

        _userPreferencesService.Update(existingPreferences => existingPreferences with
        {
            PreferredMergeAudioVideoComposeOutputFormatExtension = _selectedAudioVideoComposeOutputFormat?.Extension,
            PreferredMergeAudioVideoComposeOutputDirectory = AudioVideoComposeHasCustomOutputDirectory ? AudioVideoComposeOutputDirectory : null,
            PreferredMergeAudioVideoComposeReferenceMode = _selectedAudioVideoComposeReferenceMode,
            PreferredMergeAudioVideoComposeVideoExtendMode = _selectedAudioVideoComposeVideoExtendMode,
            PreferredMergeAudioVideoComposeImportedAudioVolumeDecibels = _audioVideoComposeImportedAudioVolumeDecibels,
            PreferredMergeAudioVideoComposeMixOriginalVideoAudio = _isAudioVideoComposeMixOriginalVideoAudioEnabled,
            PreferredMergeAudioVideoComposeOriginalVideoVolumeDecibels = _audioVideoComposeOriginalVideoVolumeDecibels,
            PreferredMergeAudioVideoComposeEnableFadeIn = _isAudioVideoComposeFadeInEnabled,
            PreferredMergeAudioVideoComposeFadeInSeconds = _audioVideoComposeFadeInSeconds,
            PreferredMergeAudioVideoComposeEnableFadeOut = _isAudioVideoComposeFadeOutEnabled,
            PreferredMergeAudioVideoComposeFadeOutSeconds = _audioVideoComposeFadeOutSeconds
        });
    }

    private void CoerceAudioVideoComposeSettings()
    {
        var shouldPersist = false;

        if (!AudioVideoComposeCanMixOriginalAudio && _isAudioVideoComposeMixOriginalVideoAudioEnabled)
        {
            _isAudioVideoComposeMixOriginalVideoAudioEnabled = false;
            OnPropertyChanged(nameof(IsAudioVideoComposeMixOriginalAudioEnabled));
            shouldPersist = true;
        }

        var normalizedFadeIn = NormalizeAudioVideoComposeFadeSeconds(_audioVideoComposeFadeInSeconds);
        if (Math.Abs(normalizedFadeIn - _audioVideoComposeFadeInSeconds) > 0.001d)
        {
            _audioVideoComposeFadeInSeconds = normalizedFadeIn;
            OnPropertyChanged(nameof(AudioVideoComposeFadeInSeconds));
            shouldPersist = true;
        }

        var normalizedFadeOut = NormalizeAudioVideoComposeFadeSeconds(_audioVideoComposeFadeOutSeconds);
        if (Math.Abs(normalizedFadeOut - _audioVideoComposeFadeOutSeconds) > 0.001d)
        {
            _audioVideoComposeFadeOutSeconds = normalizedFadeOut;
            OnPropertyChanged(nameof(AudioVideoComposeFadeOutSeconds));
            shouldPersist = true;
        }

        if (shouldPersist)
        {
            PersistAudioVideoComposePreferences();
        }
    }

    private void RaiseAudioVideoComposeStatePropertiesChanged()
    {
        CoerceAudioVideoComposeSettings();
        OnPropertyChanged(nameof(AudioVideoComposeOutputSettingsVisibility));
        OnPropertyChanged(nameof(AudioVideoComposeOutputDirectoryDisplayText));
        OnPropertyChanged(nameof(AudioVideoComposeResolvedOutputFileName));
        OnPropertyChanged(nameof(AudioVideoComposeOutputNameHintText));
        OnPropertyChanged(nameof(IsAudioVideoComposeVideoReferenceSelected));
        OnPropertyChanged(nameof(IsAudioVideoComposeAudioReferenceSelected));
        OnPropertyChanged(nameof(IsAudioVideoComposeLoopVideoExtendModeSelected));
        OnPropertyChanged(nameof(IsAudioVideoComposeFreezeFrameVideoExtendModeSelected));
        OnPropertyChanged(nameof(AudioVideoComposeCanMixOriginalAudio));
        OnPropertyChanged(nameof(AudioVideoComposeMixOriginalAudioAvailabilityText));
        OnPropertyChanged(nameof(AudioVideoComposeOriginalAudioControlsVisibility));
        OnPropertyChanged(nameof(AudioVideoComposeFadeHintText));
        OnPropertyChanged(nameof(AudioVideoComposeDurationSummaryText));
        OnPropertyChanged(nameof(AudioVideoComposeStrategySummaryText));
        OnPropertyChanged(nameof(AudioVideoComposeVideoTrackItem));
        OnPropertyChanged(nameof(AudioVideoComposeAudioTrackItem));
        OnPropertyChanged(nameof(AudioVideoComposeVideoTrackItemVisibility));
        OnPropertyChanged(nameof(AudioVideoComposeAudioTrackItemVisibility));
        OnPropertyChanged(nameof(AudioVideoComposeVideoTrackSummaryText));
        OnPropertyChanged(nameof(AudioVideoComposeAudioTrackSummaryText));
        OnPropertyChanged(nameof(AudioVideoComposeVideoDurationText));
        OnPropertyChanged(nameof(AudioVideoComposeAudioDurationText));
        OnPropertyChanged(nameof(AudioVideoComposePresetSelectionVisibility));
        OnPropertyChanged(nameof(AudioVideoComposeVideoExtendOptionsVisibility));
        OnPropertyChanged(nameof(AudioVideoComposeVideoCardStrategyText));
        OnPropertyChanged(nameof(AudioVideoComposeAudioCardStrategyText));
    }

    private AudioVideoComposeReferenceMode ResolvePreferredAudioVideoComposeReferenceMode(
        AudioVideoComposeReferenceMode preferredMode) =>
        Enum.IsDefined(typeof(AudioVideoComposeReferenceMode), preferredMode)
            ? preferredMode
            : AudioVideoComposeReferenceMode.Video;

    private AudioVideoComposeVideoExtendMode ResolvePreferredAudioVideoComposeVideoExtendMode(
        AudioVideoComposeVideoExtendMode preferredMode) =>
        Enum.IsDefined(typeof(AudioVideoComposeVideoExtendMode), preferredMode)
            ? preferredMode
            : AudioVideoComposeVideoExtendMode.Loop;

    private bool HasAudioVideoComposeAnyTrackItem() =>
        GetAudioVideoComposeVideoTrackItem() is not null ||
        GetAudioVideoComposeAudioTrackItem() is not null;

    private bool HasAudioVideoComposeAnyAudioSource() =>
        GetAudioVideoComposeAudioTrackItem() is not null ||
        GetAudioVideoComposeVideoHasEmbeddedAudio();

    private bool HasAudioVideoComposeTrackPair() =>
        GetAudioVideoComposeVideoTrackItem() is not null &&
        GetAudioVideoComposeAudioTrackItem() is not null;

    private TrackItem? GetAudioVideoComposeVideoTrackItem() =>
        _audioVideoComposeVideoTrackItems.FirstOrDefault();

    private TrackItem? GetAudioVideoComposeAudioTrackItem() =>
        _audioVideoComposeAudioTrackItems.FirstOrDefault();

    private TrackItem? GetAvailableAudioVideoComposeVideoTrackItem() =>
        GetAudioVideoComposeVideoTrackItem() is { } trackItem &&
        IsAudioVideoComposeTrackProcessable(trackItem)
            ? trackItem
            : null;

    private TrackItem? GetAvailableAudioVideoComposeAudioTrackItem() =>
        GetAudioVideoComposeAudioTrackItem() is { } trackItem &&
        IsAudioVideoComposeTrackProcessable(trackItem)
            ? trackItem
            : null;

    private static bool IsAudioVideoComposeTrackProcessable(TrackItem? trackItem) =>
        trackItem is not null &&
        !string.IsNullOrWhiteSpace(trackItem.SourcePath) &&
        (trackItem.IsSourceAvailable || File.Exists(trackItem.SourcePath));

    private bool TryGetAudioVideoComposeDurations(out TimeSpan videoDuration, out TimeSpan audioDuration)
    {
        videoDuration = TimeSpan.Zero;
        audioDuration = TimeSpan.Zero;
        return TryResolveAudioVideoComposeTrackDuration(GetAudioVideoComposeVideoTrackItem(), out videoDuration) &&
               TryResolveAudioVideoComposeTrackDuration(GetAudioVideoComposeAudioTrackItem(), out audioDuration);
    }

    private bool TryResolveAudioVideoComposeTrackDuration(TrackItem? trackItem, out TimeSpan duration)
    {
        duration = TimeSpan.Zero;
        if (trackItem is null)
        {
            return false;
        }

        if (trackItem.KnownDuration is { } knownDuration && knownDuration > TimeSpan.Zero)
        {
            duration = knownDuration;
            return true;
        }

        if (_mediaInfoService is not null &&
            !string.IsNullOrWhiteSpace(trackItem.SourcePath) &&
            _mediaInfoService.TryGetCachedDetails(trackItem.SourcePath, out var snapshot) &&
            snapshot.MediaDuration is { } mediaDuration &&
            mediaDuration > TimeSpan.Zero)
        {
            duration = mediaDuration;
            return true;
        }

        return TimeSpan.TryParse(trackItem.DurationText, out duration) && duration > TimeSpan.Zero;
    }

    private TimeSpan? GetAudioVideoComposeTargetDuration()
    {
        if (!TryGetAudioVideoComposeDurations(out var videoDuration, out var audioDuration))
        {
            return null;
        }

        if (!HasAudioVideoComposeDurationMismatch())
        {
            return videoDuration;
        }

        return GetEffectiveAudioVideoComposeReferenceMode() == AudioVideoComposeReferenceMode.Video
            ? videoDuration
            : audioDuration;
    }

    private bool HasAudioVideoComposeDurationMismatch()
    {
        if (!TryGetAudioVideoComposeDurations(out var videoDuration, out var audioDuration))
        {
            return false;
        }

        return !AreAudioVideoComposeDurationsAligned(videoDuration, audioDuration);
    }

    private bool IsAudioVideoComposeVideoShorterThanAudio() =>
        TryGetAudioVideoComposeDurations(out var videoDuration, out var audioDuration) &&
        audioDuration > videoDuration &&
        !AreAudioVideoComposeDurationsAligned(videoDuration, audioDuration);

    private AudioVideoComposeReferenceMode GetEffectiveAudioVideoComposeReferenceMode() =>
        HasAudioVideoComposeAnyTrackItem()
            ? _selectedAudioVideoComposeReferenceMode
            : AudioVideoComposeReferenceMode.Video;

    private bool GetAudioVideoComposeVideoHasEmbeddedAudio()
    {
        var videoTrackItem = GetAudioVideoComposeVideoTrackItem();
        if (videoTrackItem is null)
        {
            return false;
        }

        if (videoTrackItem.HasEmbeddedAudioStream)
        {
            return true;
        }

        if (_mediaInfoService is null || string.IsNullOrWhiteSpace(videoTrackItem.SourcePath))
        {
            return false;
        }

        var hasEmbeddedAudio = _mediaInfoService.TryGetCachedDetails(videoTrackItem.SourcePath, out var snapshot) &&
                               snapshot.HasAudioStream;
        if (hasEmbeddedAudio)
        {
            videoTrackItem.HasEmbeddedAudioStream = true;
        }

        return hasEmbeddedAudio;
    }

    private static bool AreAudioVideoComposeDurationsAligned(TimeSpan left, TimeSpan right) =>
        Math.Abs((left - right).TotalSeconds) <= AudioVideoComposeDurationToleranceSeconds;

    private string CreatePlannedAudioVideoComposeOutputPath(string videoSourcePath) =>
        MediaPathResolver.CreateUniqueOutputPath(
            MediaPathResolver.CreateMergeOutputPath(
                videoSourcePath,
                SelectedAudioVideoComposeOutputFormat.Extension,
                AudioVideoComposeHasCustomOutputDirectory ? AudioVideoComposeOutputDirectory : null,
                GetEffectiveAudioVideoComposeOutputBaseName(videoSourcePath)));

    private void EnsureAudioVideoComposeOutputDirectoryExists()
    {
        if (AudioVideoComposeHasCustomOutputDirectory)
        {
            Directory.CreateDirectory(AudioVideoComposeOutputDirectory);
        }
    }

    private string GetEffectiveAudioVideoComposeOutputBaseName() =>
        GetEffectiveAudioVideoComposeOutputBaseName(GetAudioVideoComposeVideoTrackItem()?.SourcePath);

    private string GetEffectiveAudioVideoComposeOutputBaseName(string? videoSourcePath)
    {
        if (!string.IsNullOrWhiteSpace(AudioVideoComposeOutputFileName))
        {
            return AudioVideoComposeOutputFileName;
        }

        var baseFileName = string.IsNullOrWhiteSpace(videoSourcePath)
            ? "audio_video_compose"
            : Path.GetFileNameWithoutExtension(videoSourcePath);
        return $"{baseFileName}_compose";
    }

    private string? GetDefaultAudioVideoComposeOutputDirectory()
    {
        var videoTrackItem = GetAudioVideoComposeVideoTrackItem();
        if (videoTrackItem is null || string.IsNullOrWhiteSpace(videoTrackItem.SourcePath))
        {
            return null;
        }

        try
        {
            return Path.GetDirectoryName(NormalizeSourcePath(videoTrackItem.SourcePath));
        }
        catch (Exception exception)
        {
            _logger?.Log(LogLevel.Warning, $"解析音视频合成默认输出目录失败：{videoTrackItem.SourcePath}", exception);
            return null;
        }
    }

    private string BuildAudioVideoComposeCompletionMessage(AudioVideoComposeExportRequest request)
    {
        var strategyText = request.ReferenceMode switch
        {
            AudioVideoComposeReferenceMode.Video when request.AudioDuration > request.VideoDuration =>
                "以视频为准，导入音频已自动裁剪到视频长度",
            AudioVideoComposeReferenceMode.Video when request.AudioDuration < request.VideoDuration =>
                "以视频为准，导入音频已循环补齐到视频长度",
            AudioVideoComposeReferenceMode.Audio when request.VideoDuration > request.AudioDuration =>
                "以音频为准，视频已自动裁剪到音频长度",
            AudioVideoComposeReferenceMode.Audio when request.VideoDuration < request.AudioDuration &&
                                                     request.VideoExtendMode == AudioVideoComposeVideoExtendMode.Loop =>
                "以音频为准，视频已循环延长到音频长度",
            AudioVideoComposeReferenceMode.Audio when request.VideoDuration < request.AudioDuration =>
                "以音频为准，视频已冻结最后一帧延长到音频长度",
            _ => "音视频已按原始时长自然对齐"
        };

        var mixText = request.IncludeOriginalVideoAudio
            ? $"已保留原视频声音混音（原视频 {FormatAudioVideoComposeDecibelText(request.OriginalVideoAudioVolumeDecibels)} / 导入音频 {FormatAudioVideoComposeDecibelText(request.ImportedAudioVolumeDecibels)}）"
            : $"仅使用导入音频（{FormatAudioVideoComposeDecibelText(request.ImportedAudioVolumeDecibels)}）";
        var fadeText = BuildAudioVideoComposeFadeCompletionText(request);
        return $"音视频合成完成：{Path.GetFileName(request.OutputPath)}。{strategyText}；输出时长 {FormatDuration(request.OutputDuration)}。{mixText}{fadeText}";
    }

    private void UpdateAudioVideoComposeProgress(FFmpegProgressUpdate progress)
    {
        if (progress.IsCompleted)
        {
            return;
        }

        if (progress.ProgressRatio is not double ratio)
        {
            StatusMessage = "正在合成音视频，FFmpeg 正在返回实时进度...";
            return;
        }

        var normalized = Math.Clamp(ratio, 0d, 1d);
        var percentText = $"{Math.Round(normalized * 100d):0}%";
        StatusMessage = progress.ProcessedDuration is { } processedDuration && progress.TotalDuration is { } totalDuration
            ? $"正在合成音视频：{percentText}（{FormatDuration(processedDuration)} / {FormatDuration(totalDuration)}）"
            : $"正在合成音视频：{percentText}";
    }

    private static string BuildAudioVideoComposeFadeCompletionText(AudioVideoComposeExportRequest request)
    {
        if (!request.EnableImportedAudioFadeIn && !request.EnableImportedAudioFadeOut)
        {
            return string.Empty;
        }

        if (request.EnableImportedAudioFadeIn && request.EnableImportedAudioFadeOut)
        {
            return $"；导入音频淡入 {FormatAudioVideoComposeSeconds(request.ImportedAudioFadeInDuration.TotalSeconds)}，淡出 {FormatAudioVideoComposeSeconds(request.ImportedAudioFadeOutDuration.TotalSeconds)}";
        }

        return request.EnableImportedAudioFadeIn
            ? $"；导入音频淡入 {FormatAudioVideoComposeSeconds(request.ImportedAudioFadeInDuration.TotalSeconds)}"
            : $"；导入音频淡出 {FormatAudioVideoComposeSeconds(request.ImportedAudioFadeOutDuration.TotalSeconds)}";
    }

    private static double NormalizeAudioVideoComposeDecibels(double value) =>
        Math.Round(Math.Clamp(value, AudioVideoComposeMinimumDecibels, AudioVideoComposeMaximumDecibels), 1, MidpointRounding.AwayFromZero);

    private double NormalizeAudioVideoComposeFadeSeconds(double value)
    {
        var normalized = Math.Max(0d, Math.Round(value, 1, MidpointRounding.AwayFromZero));
        var targetDuration = GetAudioVideoComposeTargetDuration();
        return targetDuration is { } duration && duration > TimeSpan.Zero
            ? Math.Min(normalized, Math.Round(duration.TotalSeconds, 1, MidpointRounding.AwayFromZero))
            : normalized;
    }

    private static string FormatAudioVideoComposeDecibelText(double value)
    {
        var rounded = Math.Round(value, 1, MidpointRounding.AwayFromZero);
        return rounded > 0d
            ? $"+{rounded:0.#} dB"
            : $"{rounded:0.#} dB";
    }

    private static string FormatAudioVideoComposeSeconds(double seconds) =>
        $"{Math.Round(seconds, 1, MidpointRounding.AwayFromZero):0.#} 秒";
}
