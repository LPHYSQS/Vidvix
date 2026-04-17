using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.UI.Xaml;
using Vidvix.Core.Interfaces;
using Vidvix.Core.Models;
using Vidvix.Utils;

namespace Vidvix.ViewModels;

public sealed partial class MergeViewModel : ObservableObject
{
    private readonly ObservableCollection<MediaItem> _mediaItems;
    private readonly ObservableCollection<TrackItem> _videoJoinVideoTrackItems;
    private readonly ObservableCollection<TrackItem> _audioJoinAudioTrackItems;
    private readonly ObservableCollection<TrackItem> _audioVideoComposeVideoTrackItems;
    private readonly ObservableCollection<TrackItem> _audioVideoComposeAudioTrackItems;
    private readonly ObservableCollection<TrackItem> _emptyTrackItems;
    private readonly IReadOnlyDictionary<MergeWorkspaceMode, MergeWorkspaceModeState> _modeStates;
    private readonly IReadOnlyList<OutputFormatOption> _videoJoinOutputFormats;
    private readonly IReadOnlyList<OutputFormatOption> _audioJoinOutputFormats;
    private readonly AsyncRelayCommand _importFilesCommand;
    private readonly AsyncRelayCommand _browseOutputDirectoryCommand;
    private readonly RelayCommand _clearOutputDirectoryCommand;
    private readonly AsyncRelayCommand _startVideoJoinProcessingCommand;
    private readonly RelayCommand _cancelVideoJoinProcessingCommand;
    private readonly IFilePickerService? _filePickerService;
    private readonly IMergeMediaAnalysisService? _mergeMediaAnalysisService;
    private readonly IVideoJoinWorkflowService? _videoJoinWorkflowService;
    private readonly IAudioJoinWorkflowService? _audioJoinWorkflowService;
    private readonly IFileRevealService? _fileRevealService;
    private readonly IMediaInfoService? _mediaInfoService;
    private readonly IUserPreferencesService? _userPreferencesService;
    private readonly ILogger? _logger;
    private readonly HashSet<string> _supportedVideoInputFileTypes;
    private readonly HashSet<string> _supportedAudioInputFileTypes;
    private readonly IReadOnlyList<string> _supportedImportFileTypes;
    private CancellationTokenSource? _videoJoinProcessingCancellationSource;
    private bool _isNormalizingAudioVideoComposeTrackCollection;

    private OutputFormatOption? _selectedVideoJoinOutputFormat;
    private OutputFormatOption? _selectedAudioJoinOutputFormat;
    private string _videoJoinOutputDirectory;
    private string _audioJoinOutputDirectory;
    private string _videoJoinOutputFileName;
    private string _audioJoinOutputFileName;
    private string _statusMessage;
    private string _modeMismatchWarningMessage;
    private bool _isModeMismatchWarningVisible;
    private bool _isVideoJoinProcessing;
    private MergeWorkspaceMode _selectedMergeMode;
    private MergeSmallerResolutionStrategy _selectedSmallerResolutionStrategy;
    private MergeLargerResolutionStrategy _selectedLargerResolutionStrategy;
    private Guid? _manualVideoResolutionPresetTrackId;
    private Guid? _manualAudioParameterPresetTrackId;
    private AudioJoinParameterMode _selectedAudioJoinParameterMode;

    public MergeViewModel(
        IFilePickerService? filePickerService = null,
        IMediaInfoService? mediaInfoService = null,
        IUserPreferencesService? userPreferencesService = null,
        IMergeMediaAnalysisService? mergeMediaAnalysisService = null,
        IVideoJoinWorkflowService? videoJoinWorkflowService = null,
        IAudioJoinWorkflowService? audioJoinWorkflowService = null,
        IAudioVideoComposeWorkflowService? audioVideoComposeWorkflowService = null,
        IFileRevealService? fileRevealService = null,
        ApplicationConfiguration? configuration = null,
        ILogger? logger = null)
    {
        var effectiveConfiguration = configuration ?? new ApplicationConfiguration();
        var preferences = userPreferencesService?.Load() ?? new UserPreferences();

        _filePickerService = filePickerService;
        _mergeMediaAnalysisService = mergeMediaAnalysisService;
        _videoJoinWorkflowService = videoJoinWorkflowService;
        _audioJoinWorkflowService = audioJoinWorkflowService;
        _audioVideoComposeWorkflowService = audioVideoComposeWorkflowService;
        _fileRevealService = fileRevealService;
        _mediaInfoService = mediaInfoService;
        _userPreferencesService = userPreferencesService;
        _logger = logger;
        _supportedVideoInputFileTypes = effectiveConfiguration.SupportedVideoInputFileTypes.ToHashSet(StringComparer.OrdinalIgnoreCase);
        _supportedAudioInputFileTypes = effectiveConfiguration.SupportedAudioInputFileTypes.ToHashSet(StringComparer.OrdinalIgnoreCase);
        _supportedImportFileTypes = effectiveConfiguration.SupportedTrimInputFileTypes
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        _mediaItems = new ObservableCollection<MediaItem>();
        _videoJoinVideoTrackItems = new ObservableCollection<TrackItem>();
        _audioJoinAudioTrackItems = new ObservableCollection<TrackItem>();
        _audioVideoComposeVideoTrackItems = new ObservableCollection<TrackItem>();
        _audioVideoComposeAudioTrackItems = new ObservableCollection<TrackItem>();
        _emptyTrackItems = new ObservableCollection<TrackItem>();
        _videoJoinOutputFormats = effectiveConfiguration.SupportedVideoOutputFormats;
        _audioJoinOutputFormats = effectiveConfiguration.SupportedAudioOutputFormats;
        _modeStates = CreateModeStates(effectiveConfiguration);

        _selectedVideoJoinOutputFormat = ResolvePreferredVideoJoinOutputFormat(preferences.PreferredMergeVideoJoinOutputFormatExtension);
        _selectedAudioJoinOutputFormat = ResolvePreferredAudioJoinOutputFormat(preferences.PreferredMergeAudioJoinOutputFormatExtension);
        _videoJoinOutputDirectory = NormalizeOutputDirectory(preferences.PreferredMergeVideoJoinOutputDirectory);
        _audioJoinOutputDirectory = NormalizeOutputDirectory(preferences.PreferredMergeAudioJoinOutputDirectory);
        _videoJoinOutputFileName = string.Empty;
        _audioJoinOutputFileName = string.Empty;
        _statusMessage = "请先导入视频或音频素材，再将它们添加到对应轨道。";
        _modeMismatchWarningMessage = string.Empty;
        _selectedMergeMode = ResolvePreferredMergeMode(preferences.PreferredMergeWorkspaceMode);
        _selectedSmallerResolutionStrategy = ResolvePreferredMergeSmallerResolutionStrategy(preferences.PreferredMergeSmallerResolutionStrategy);
        _selectedLargerResolutionStrategy = ResolvePreferredMergeLargerResolutionStrategy(preferences.PreferredMergeLargerResolutionStrategy);
        _selectedAudioJoinParameterMode = ResolvePreferredAudioJoinParameterMode(preferences.PreferredMergeAudioJoinParameterMode);
        InitializeAudioVideoComposeState(preferences);

        _importFilesCommand = new AsyncRelayCommand(ImportFilesAsync, () => !IsVideoJoinProcessing);
        _browseOutputDirectoryCommand = new AsyncRelayCommand(BrowseOutputDirectoryAsync, CanEditActiveOutputSettings);
        _clearOutputDirectoryCommand = new RelayCommand(ClearOutputDirectory, CanClearOutputDirectory);
        _startVideoJoinProcessingCommand = new AsyncRelayCommand(StartProcessingAsync, CanStartProcessing);
        _cancelVideoJoinProcessingCommand = new RelayCommand(CancelProcessing, () => IsVideoJoinProcessing);

        _mediaItems.CollectionChanged += OnMediaItemsChanged;
        _videoJoinVideoTrackItems.CollectionChanged += OnTrackItemsChanged;
        _audioJoinAudioTrackItems.CollectionChanged += OnTrackItemsChanged;
        _audioVideoComposeVideoTrackItems.CollectionChanged += OnTrackItemsChanged;
        _audioVideoComposeAudioTrackItems.CollectionChanged += OnTrackItemsChanged;
    }

    public event Action<string, string>? InvalidTrackItemsDetected;

    private MergeWorkspaceModeState CurrentModeState => GetModeState(_selectedMergeMode);

    private ObservableCollection<TrackItem> ActiveVideoTrackItems => CurrentModeState.VideoTrackItems;

    private ObservableCollection<TrackItem> ActiveAudioTrackItems => CurrentModeState.AudioTrackItems;

    public ObservableCollection<MediaItem> MediaItems => _mediaItems;

    public ObservableCollection<TrackItem> VideoTrackItems => ActiveVideoTrackItems;

    public ObservableCollection<TrackItem> AudioTrackItems => ActiveAudioTrackItems;

    public IReadOnlyList<OutputFormatOption> VideoJoinOutputFormats => _videoJoinOutputFormats;

    public IReadOnlyList<OutputFormatOption> AudioJoinOutputFormats => _audioJoinOutputFormats;

    public ICommand ImportFilesCommand => _importFilesCommand;

    public ICommand BrowseOutputDirectoryCommand => _browseOutputDirectoryCommand;

    public ICommand ClearOutputDirectoryCommand => _clearOutputDirectoryCommand;

    public ICommand StartVideoJoinProcessingCommand => _startVideoJoinProcessingCommand;

    public ICommand CancelVideoJoinProcessingCommand => _cancelVideoJoinProcessingCommand;

    public ICommand StartMergeProcessingCommand => _startVideoJoinProcessingCommand;

    public ICommand CancelMergeProcessingCommand => _cancelVideoJoinProcessingCommand;

    public OutputFormatOption SelectedOutputFormat
    {
        get => _selectedVideoJoinOutputFormat ?? _videoJoinOutputFormats.First();
        set
        {
            if (value is null)
            {
                return;
            }

            if (SetProperty(ref _selectedVideoJoinOutputFormat, value))
            {
                OnPropertyChanged(nameof(SelectedOutputFormatDescription));
                OnPropertyChanged(nameof(VideoJoinResolvedOutputFileName));
                OnPropertyChanged(nameof(VideoJoinOutputNameHintText));
                PersistVideoJoinPreferences();
                StatusMessage = $"视频拼接输出格式已切换为 {value.DisplayName}。";
            }
        }
    }

    public string SelectedOutputFormatDescription => SelectedOutputFormat.Description;

    public string OutputDirectory
    {
        get => _videoJoinOutputDirectory;
        set
        {
            var normalizedDirectory = NormalizeOutputDirectory(value);
            if (SetProperty(ref _videoJoinOutputDirectory, normalizedDirectory))
            {
                OnPropertyChanged(nameof(HasCustomOutputDirectory));
                OnPropertyChanged(nameof(VideoJoinOutputDirectoryDisplayText));
                PersistVideoJoinPreferences();
                NotifyCommandStates();
            }
        }
    }

    public bool HasCustomOutputDirectory => !string.IsNullOrWhiteSpace(OutputDirectory);

    public string VideoJoinOutputDirectoryDisplayText =>
        HasCustomOutputDirectory
            ? OutputDirectory
            : GetDefaultVideoJoinOutputDirectory() ?? string.Empty;

    public string OutputDirectoryHintText => "默认使用当前模式下基准素材所在文件夹；设置后，处理结果会统一输出到所选文件夹。";

    public string VideoJoinOutputFileName
    {
        get => _videoJoinOutputFileName;
        set
        {
            var normalizedValue = NormalizeVideoJoinOutputFileName(value);
            if (SetProperty(ref _videoJoinOutputFileName, normalizedValue))
            {
                OnPropertyChanged(nameof(VideoJoinResolvedOutputFileName));
                OnPropertyChanged(nameof(VideoJoinOutputNameHintText));
            }
        }
    }

    public string VideoJoinResolvedOutputFileName => $"{GetEffectiveVideoJoinOutputBaseName()}{SelectedOutputFormat.Extension}";

    public string VideoJoinOutputNameHintText =>
        $"{(string.IsNullOrWhiteSpace(VideoJoinOutputFileName) ? "留空时默认使用" : "当前将输出为")} {VideoJoinResolvedOutputFileName}；若目标目录中已存在同名文件，系统会自动追加序号，避免覆盖原始文件。";

    public OutputFormatOption SelectedAudioJoinOutputFormat
    {
        get => _selectedAudioJoinOutputFormat ?? _audioJoinOutputFormats.First();
        set
        {
            if (value is null)
            {
                return;
            }

            if (SetProperty(ref _selectedAudioJoinOutputFormat, value))
            {
                OnPropertyChanged(nameof(SelectedAudioJoinOutputFormatDescription));
                OnPropertyChanged(nameof(AudioJoinResolvedOutputFileName));
                OnPropertyChanged(nameof(AudioJoinOutputNameHintText));
                OnPropertyChanged(nameof(AudioParameterPresetSummaryText));
                OnPropertyChanged(nameof(AudioJoinParameterModeHintText));
                PersistAudioJoinPreferences();
                StatusMessage = $"音频拼接输出格式已切换为 {value.DisplayName}。";
            }
        }
    }

    public string SelectedAudioJoinOutputFormatDescription => SelectedAudioJoinOutputFormat.Description;

    public string AudioJoinOutputDirectory
    {
        get => _audioJoinOutputDirectory;
        set
        {
            var normalizedDirectory = NormalizeOutputDirectory(value);
            if (SetProperty(ref _audioJoinOutputDirectory, normalizedDirectory))
            {
                OnPropertyChanged(nameof(AudioJoinHasCustomOutputDirectory));
                OnPropertyChanged(nameof(AudioJoinOutputDirectoryDisplayText));
                PersistAudioJoinPreferences();
                NotifyCommandStates();
            }
        }
    }

    public bool AudioJoinHasCustomOutputDirectory => !string.IsNullOrWhiteSpace(AudioJoinOutputDirectory);

    public string AudioJoinOutputDirectoryDisplayText =>
        AudioJoinHasCustomOutputDirectory
            ? AudioJoinOutputDirectory
            : GetDefaultAudioJoinOutputDirectory() ?? string.Empty;

    public string AudioJoinOutputFileName
    {
        get => _audioJoinOutputFileName;
        set
        {
            var normalizedValue = NormalizeOutputFileName(value);
            if (SetProperty(ref _audioJoinOutputFileName, normalizedValue))
            {
                OnPropertyChanged(nameof(AudioJoinResolvedOutputFileName));
                OnPropertyChanged(nameof(AudioJoinOutputNameHintText));
            }
        }
    }

    public string AudioJoinResolvedOutputFileName => $"{GetEffectiveAudioJoinOutputBaseName()}{SelectedAudioJoinOutputFormat.Extension}";

    public string AudioJoinOutputNameHintText =>
        $"{(string.IsNullOrWhiteSpace(AudioJoinOutputFileName) ? "留空时默认使用" : "当前将输出为")} {AudioJoinResolvedOutputFileName}；若目标目录中已存在同名文件，系统会自动追加序号，避免覆盖原始文件。";

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public bool IsVideoJoinProcessing
    {
        get => _isVideoJoinProcessing;
        private set
        {
            if (SetProperty(ref _isVideoJoinProcessing, value))
            {
                OnPropertyChanged(nameof(CanModifyWorkspace));
                OnPropertyChanged(nameof(WorkspaceInteractionShieldVisibility));
                NotifyCommandStates();
            }
        }
    }

    public Visibility VideoJoinOutputSettingsVisibility =>
        IsVideoJoinModeSelected ? Visibility.Visible : Visibility.Collapsed;

    public Visibility AudioJoinOutputSettingsVisibility =>
        IsAudioJoinModeSelected ? Visibility.Visible : Visibility.Collapsed;

    public Visibility NonVideoJoinOutputSettingsVisibility =>
        IsAudioVideoComposeModeSelected ? Visibility.Visible : Visibility.Collapsed;

    public Visibility ModeMismatchWarningVisibility =>
        _isModeMismatchWarningVisible ? Visibility.Visible : Visibility.Collapsed;

    public string ModeMismatchWarningMessage => _modeMismatchWarningMessage;

    public bool IsVideoJoinModeSelected
    {
        get => _selectedMergeMode == MergeWorkspaceMode.VideoJoin;
        set
        {
            if (value)
            {
                SetMergeMode(MergeWorkspaceMode.VideoJoin);
            }
        }
    }

    public bool IsAudioJoinModeSelected
    {
        get => _selectedMergeMode == MergeWorkspaceMode.AudioJoin;
        set
        {
            if (value)
            {
                SetMergeMode(MergeWorkspaceMode.AudioJoin);
            }
        }
    }

    public bool IsAudioVideoComposeModeSelected
    {
        get => _selectedMergeMode == MergeWorkspaceMode.AudioVideoCompose;
        set
        {
            if (value)
            {
                SetMergeMode(MergeWorkspaceMode.AudioVideoCompose);
            }
        }
    }

    public bool IsBalancedAudioJoinParameterModeSelected
    {
        get => _selectedAudioJoinParameterMode == AudioJoinParameterMode.Balanced;
        set
        {
            if (value)
            {
                SetAudioJoinParameterMode(AudioJoinParameterMode.Balanced);
            }
        }
    }

    public bool IsPresetAudioJoinParameterModeSelected
    {
        get => _selectedAudioJoinParameterMode == AudioJoinParameterMode.Preset;
        set
        {
            if (value)
            {
                SetAudioJoinParameterMode(AudioJoinParameterMode.Preset);
            }
        }
    }

    public Visibility AudioJoinPresetSelectionVisibility =>
        _selectedAudioJoinParameterMode == AudioJoinParameterMode.Preset
            ? Visibility.Visible
            : Visibility.Collapsed;

    public string AudioTrackOperationHintText =>
        _selectedAudioJoinParameterMode == AudioJoinParameterMode.Preset
            ? "可左键长按拖拽片段调整顺序，也可使用片段顶部按钮快速移除或设为参数预设。"
            : "可左键长按拖拽片段调整顺序，也可使用片段顶部按钮快速移除。均衡模式不支持手动参数预设。";

    public string AudioJoinParameterModeHintText =>
        _selectedAudioJoinParameterMode == AudioJoinParameterMode.Preset
            ? $"指定预设模式会锁定当前参数预设片段的采样率与目标码率：低于目标的音频会补齐到目标，高于目标的会压到目标，匹配的保持不动。若 {SelectedAudioJoinOutputFormat.DisplayName} 本身不支持固定码率，系统会优先锁定采样率并尽可能贴近目标码率。"
            : $"均衡模式不会锁定某一个预设片段。系统会按全部有效音轨中最常见的采样率与码率统一处理，再根据 {SelectedAudioJoinOutputFormat.DisplayName} 的编码规则做兼容性量化，因此重新导入后看到的 kHz / kbps 可能不会与某条原始音频完全一致。";

    public bool IsPadSmallerVideoWithBlackBarsSelected
    {
        get => _selectedSmallerResolutionStrategy == MergeSmallerResolutionStrategy.PadWithBlackBars;
        set
        {
            if (value)
            {
                SetSmallerResolutionStrategy(MergeSmallerResolutionStrategy.PadWithBlackBars);
            }
        }
    }

    public bool IsStretchSmallerVideoToFillSelected
    {
        get => _selectedSmallerResolutionStrategy == MergeSmallerResolutionStrategy.StretchToFill;
        set
        {
            if (value)
            {
                SetSmallerResolutionStrategy(MergeSmallerResolutionStrategy.StretchToFill);
            }
        }
    }

    public bool IsSqueezeLargerVideoToFitSelected
    {
        get => _selectedLargerResolutionStrategy == MergeLargerResolutionStrategy.SqueezeToFit;
        set
        {
            if (value)
            {
                SetLargerResolutionStrategy(MergeLargerResolutionStrategy.SqueezeToFit);
            }
        }
    }

    public bool IsCropLargerVideoToFillSelected
    {
        get => _selectedLargerResolutionStrategy == MergeLargerResolutionStrategy.CropToFill;
        set
        {
            if (value)
            {
                SetLargerResolutionStrategy(MergeLargerResolutionStrategy.CropToFill);
            }
        }
    }

    public string TimelineHintText => CurrentModeState.Profile.TimelineHintText;

    public string VideoTrackSummaryText => ActiveVideoTrackItems.Count == 0
        ? "未添加片段"
        : $"{ActiveVideoTrackItems.Count} 个片段";

    public string VideoJoinTotalDurationText
    {
        get
        {
            var activeTrackItems = _videoJoinVideoTrackItems
                .Where(trackItem => trackItem.IsSourceAvailable)
                .ToArray();
            if (activeTrackItems.Length == 0)
            {
                return "总时长 · 00:00:00";
            }

            var totalDuration = TimeSpan.Zero;
            var hasUnknownDuration = false;
            foreach (var trackItem in activeTrackItems)
            {
                if (TryParseTrackDuration(trackItem.DurationText, out var duration))
                {
                    totalDuration += duration;
                }
                else
                {
                    hasUnknownDuration = true;
                }
            }

            var suffix = hasUnknownDuration ? "+" : string.Empty;
            return $"总时长 · {FormatDuration(totalDuration)}{suffix}";
        }
    }

    public string AudioJoinTotalDurationText
    {
        get
        {
            var activeTrackItems = _audioJoinAudioTrackItems
                .Where(trackItem => trackItem.IsSourceAvailable)
                .ToArray();
            if (activeTrackItems.Length == 0)
            {
                return "总时长 · 00:00:00";
            }

            var totalDuration = TimeSpan.Zero;
            var hasUnknownDuration = false;
            foreach (var trackItem in activeTrackItems)
            {
                if (TryParseTrackDuration(trackItem.DurationText, out var duration))
                {
                    totalDuration += duration;
                }
                else
                {
                    hasUnknownDuration = true;
                }
            }

            var suffix = hasUnknownDuration ? "+" : string.Empty;
            return $"总时长 · {FormatDuration(totalDuration)}{suffix}";
        }
    }

    public string AudioTrackSummaryText => ActiveAudioTrackItems.Count == 0
        ? "未添加片段"
        : $"{ActiveAudioTrackItems.Count} 个片段";

    public Visibility MediaItemsEmptyVisibility => _mediaItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    public Visibility VideoTrackEmptyVisibility => ActiveVideoTrackItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    public Visibility AudioTrackEmptyVisibility => ActiveAudioTrackItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    public Visibility VideoJoinTimelineVisibility =>
        CurrentModeState.Profile.ShowsVideoJoinTimeline ? Visibility.Visible : Visibility.Collapsed;

    public Visibility AudioJoinTimelineVisibility =>
        CurrentModeState.Profile.ShowsAudioJoinTimeline ? Visibility.Visible : Visibility.Collapsed;

    public Visibility StandardTimelineVisibility =>
        CurrentModeState.Profile.ShowsStandardTimeline ? Visibility.Visible : Visibility.Collapsed;

    public string VideoResolutionPresetSummaryText
    {
        get
        {
            if (_videoJoinVideoTrackItems.Count == 0)
            {
                return "添加视频片段后，将默认以首个可用视频作为分辨率预设。";
            }

            var presetTrackItem = GetEffectiveVideoResolutionPresetItem();
            if (presetTrackItem is null)
            {
                return "当前暂无可用的视频片段可作为分辨率预设。";
            }

            return HasManualVideoResolutionPresetSelection()
                ? $"当前分辨率预设：{presetTrackItem.SourceName}"
                : $"当前默认以首个可用视频作为分辨率预设：{presetTrackItem.SourceName}";
        }
    }

    public string AudioParameterPresetSummaryText
    {
        get
        {
            if (_selectedAudioJoinParameterMode == AudioJoinParameterMode.Balanced)
            {
                if (_audioJoinAudioTrackItems.Count == 0)
                {
                    return "当前为均衡模式，添加音频片段后会按全部有效音轨自动统一参数。";
                }

                var (targetSampleRate, targetBitrate) = ResolveBalancedAudioJoinTargets(_audioJoinAudioTrackItems);
                return $"当前为均衡模式：不使用手动参数预设，预计输出 {BuildAudioJoinOutputSummaryText(targetSampleRate, targetBitrate, SelectedAudioJoinOutputFormat, AudioJoinParameterMode.Balanced)}。";
            }

            if (_audioJoinAudioTrackItems.Count == 0)
            {
                return "添加音频片段后，将默认以首个可用音频作为参数预设。";
            }

            var presetTrackItem = GetEffectiveAudioParameterPresetItem();
            if (presetTrackItem is null)
            {
                return "当前暂无可用的音频片段可作为参数预设。";
            }

            var sampleRate = MergeMediaMetadataParser.TryResolveAudioJoinSampleRate(null, presetTrackItem, out var resolvedSampleRate)
                ? resolvedSampleRate
                : 0;
            int? bitrate = MergeMediaMetadataParser.TryResolveAudioJoinBitrate(null, presetTrackItem, out var resolvedBitrate)
                ? resolvedBitrate
                : null;

            return HasManualAudioParameterPresetSelection()
                ? $"当前音频参数预设：{presetTrackItem.SourceName} · 目标输出 {BuildAudioJoinOutputSummaryText(sampleRate, bitrate, SelectedAudioJoinOutputFormat, AudioJoinParameterMode.Preset)}。"
                : $"当前默认以首个可用音频作为参数预设：{presetTrackItem.SourceName} · 目标输出 {BuildAudioJoinOutputSummaryText(sampleRate, bitrate, SelectedAudioJoinOutputFormat, AudioJoinParameterMode.Preset)}。";
        }
    }

    public string VideoTrackEmptyText => CurrentModeState.Profile.VideoTrackEmptyText;

    public string AudioTrackEmptyText => CurrentModeState.Profile.AudioTrackEmptyText;

    public void AddMediaToTimeline(MediaItem mediaItem)
    {
        ArgumentNullException.ThrowIfNull(mediaItem);

        if (IsVideoJoinProcessing)
        {
            StatusMessage = "当前合并任务处理中，若需调整轨道，请先取消当前任务。";
            return;
        }

        if (!TryResolveTrackCollectionForAddition(mediaItem, out var trackItems, out var rejectionMessage))
        {
            SetModeMismatchWarningVisibility(true, rejectionMessage);
            StatusMessage = rejectionMessage;
            return;
        }

        SetModeMismatchWarningVisibility(false, string.Empty);
        if ((mediaItem.IsVideo && CurrentModeState.Profile.ReplaceVideoTrackOnAdd) ||
            (mediaItem.IsAudio && CurrentModeState.Profile.ReplaceAudioTrackOnAdd))
        {
            AddMediaToAudioVideoComposeTimeline(mediaItem, trackItems);
            return;
        }

        trackItems.Add(CreateTrackItem(mediaItem, trackItems.Count + 1, IsSourcePathAvailable(mediaItem.SourcePath)));
        StatusMessage = mediaItem.IsVideo
            ? $"{mediaItem.FileName} 已加入视频轨道。"
            : $"{mediaItem.FileName} 已加入音频轨道。";
    }

    public void RemoveMediaItem(MediaItem mediaItem)
    {
        ArgumentNullException.ThrowIfNull(mediaItem);

        if (IsVideoJoinProcessing)
        {
            StatusMessage = "当前合并任务处理中，若需调整素材，请先取消当前任务。";
            return;
        }

        var normalizedSourcePath = NormalizeSourcePath(mediaItem.SourcePath);
        var invalidatedTrackItems = string.IsNullOrWhiteSpace(normalizedSourcePath)
            ? Array.Empty<TrackItem>()
            : GetAllTrackCollections()
                .SelectMany(collection => collection)
                .Where(trackItem => trackItem.IsSourceAvailable && IsSameSource(trackItem.SourcePath, normalizedSourcePath))
                .ToArray();

        if (!_mediaItems.Remove(mediaItem))
        {
            return;
        }

        if (invalidatedTrackItems.Length == 0)
        {
            StatusMessage = $"已从素材列表移除 {mediaItem.FileName}。";
            return;
        }

        var notificationMessage = BuildInvalidTrackItemsMessage(invalidatedTrackItems.Length);
        StatusMessage = notificationMessage;
        InvalidTrackItemsDetected?.Invoke("轨道片段已标记为失效", notificationMessage);
    }

    public void RemoveVideoTrackItem(TrackItem trackItem)
    {
        ArgumentNullException.ThrowIfNull(trackItem);

        if (IsVideoJoinProcessing)
        {
            StatusMessage = "当前合并任务处理中，若需调整轨道，请先取消当前任务。";
            return;
        }

        var targetCollection = _videoJoinVideoTrackItems.Contains(trackItem)
            ? _videoJoinVideoTrackItems
            : _audioVideoComposeVideoTrackItems.Contains(trackItem)
                ? _audioVideoComposeVideoTrackItems
                : null;
        if (targetCollection is null)
        {
            return;
        }

        var removedClipName = trackItem.SourceName;
        if (ReferenceEquals(targetCollection, _audioVideoComposeVideoTrackItems))
        {
            targetCollection.Remove(trackItem);
            StatusMessage = $"{removedClipName} 已从音视频合成的视频轨道移除。";
            return;
        }

        var removedPresetClip = ReferenceEquals(GetEffectiveVideoResolutionPresetItem(), trackItem);
        targetCollection.Remove(trackItem);

        var fallbackPresetTrackItem = GetEffectiveVideoResolutionPresetItem();
        StatusMessage = removedPresetClip && fallbackPresetTrackItem is not null
            ? $"{removedClipName} 已从视频轨道移除，分辨率预设已切换为 {fallbackPresetTrackItem.SourceName}。"
            : $"{removedClipName} 已从视频轨道移除。";
    }

    public void SetVideoResolutionPreset(TrackItem trackItem)
    {
        ArgumentNullException.ThrowIfNull(trackItem);

        if (IsVideoJoinProcessing)
        {
            StatusMessage = "视频拼接任务处理中，若需调整分辨率预设，请先取消当前任务。";
            return;
        }

        if (!_videoJoinVideoTrackItems.Contains(trackItem))
        {
            return;
        }

        if (!trackItem.CanSetAsResolutionPreset)
        {
            StatusMessage = "当前片段引用的源素材已失效，无法设为分辨率预设。";
            return;
        }

        _manualVideoResolutionPresetTrackId = trackItem.TrackId;
        RefreshTrackCollection(_videoJoinVideoTrackItems, supportsVideoPreset: true);
        RaiseTrackStatePropertiesChanged();
        StatusMessage = $"{trackItem.SourceName} 已设为分辨率预设。";
    }

    public void RemoveAudioTrackItem(TrackItem trackItem)
    {
        ArgumentNullException.ThrowIfNull(trackItem);

        if (IsVideoJoinProcessing)
        {
            StatusMessage = "当前合并任务处理中，若需调整轨道，请先取消当前任务。";
            return;
        }

        var targetCollection = _audioJoinAudioTrackItems.Contains(trackItem)
            ? _audioJoinAudioTrackItems
            : _audioVideoComposeAudioTrackItems.Contains(trackItem)
                ? _audioVideoComposeAudioTrackItems
                : null;
        if (targetCollection is null)
        {
            return;
        }

        var removedClipName = trackItem.SourceName;
        if (ReferenceEquals(targetCollection, _audioVideoComposeAudioTrackItems))
        {
            targetCollection.Remove(trackItem);
            StatusMessage = $"{removedClipName} 已从音视频合成的音频轨道移除。";
            return;
        }

        var removedPresetClip = _selectedAudioJoinParameterMode == AudioJoinParameterMode.Preset &&
                                 ReferenceEquals(GetEffectiveAudioParameterPresetItem(), trackItem);
        targetCollection.Remove(trackItem);

        if (_selectedAudioJoinParameterMode != AudioJoinParameterMode.Preset)
        {
            StatusMessage = $"{removedClipName} 已从音频轨道移除。";
            return;
        }

        var fallbackPresetTrackItem = GetEffectiveAudioParameterPresetItem();
        StatusMessage = removedPresetClip && fallbackPresetTrackItem is not null
            ? $"{removedClipName} 已从音频轨道移除，参数预设已切换为 {fallbackPresetTrackItem.SourceName}。"
            : $"{removedClipName} 已从音频轨道移除。";
    }

    public void SetAudioParameterPreset(TrackItem trackItem)
    {
        ArgumentNullException.ThrowIfNull(trackItem);

        if (IsVideoJoinProcessing)
        {
            StatusMessage = "音频拼接任务处理中，若需调整参数预设，请先取消当前任务。";
            return;
        }

        if (_selectedAudioJoinParameterMode != AudioJoinParameterMode.Preset)
        {
            StatusMessage = "当前为均衡模式，不支持手动参数预设。";
            return;
        }

        if (!_audioJoinAudioTrackItems.Contains(trackItem))
        {
            return;
        }

        if (!trackItem.CanSetAsResolutionPreset)
        {
            StatusMessage = "当前片段引用的源素材已失效，无法设为参数预设。";
            return;
        }

        _manualAudioParameterPresetTrackId = trackItem.TrackId;
        RefreshTrackCollection(_audioJoinAudioTrackItems, supportsVideoPreset: false);
        RaiseTrackStatePropertiesChanged();
        StatusMessage = $"{trackItem.SourceName} 已设为音频参数预设。";
    }

    private async Task ImportFilesAsync()
    {
        if (_filePickerService is null)
        {
            StatusMessage = "当前环境暂不支持打开文件选择器。";
            return;
        }

        try
        {
            var selectedFiles = await _filePickerService.PickFilesAsync(
                new FilePickerRequest(_supportedImportFileTypes, "导入文件"));

            if (selectedFiles.Count == 0)
            {
                StatusMessage = "已取消文件导入。";
                return;
            }

            var existingPaths = new HashSet<string>(
                _mediaItems
                    .Select(item => item.SourcePath)
                    .Where(path => !string.IsNullOrWhiteSpace(path))
                    .Select(NormalizeSourcePath),
                StringComparer.OrdinalIgnoreCase);

            var addedCount = 0;
            var duplicateCount = 0;

            foreach (var selectedFile in selectedFiles)
            {
                if (string.IsNullOrWhiteSpace(selectedFile))
                {
                    continue;
                }

                var filePath = NormalizeSourcePath(selectedFile);
                if (!existingPaths.Add(filePath))
                {
                    duplicateCount++;
                    continue;
                }

                _mediaItems.Add(await CreateMediaItemAsync(filePath));
                addedCount++;
            }

            if (addedCount > 0)
            {
                StatusMessage = duplicateCount > 0
                    ? $"已导入 {addedCount} 个素材，{duplicateCount} 个重复文件已跳过。"
                    : $"已导入 {addedCount} 个素材，列表按导入顺序排列。";
                return;
            }

            StatusMessage = duplicateCount > 0
                ? "所选文件已存在于素材列表中。"
                : "未导入新的素材。";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "已取消文件导入。";
        }
        catch (Exception exception)
        {
            StatusMessage = "导入文件失败，请稍后重试。";
            _logger?.Log(LogLevel.Error, "导入合并素材时发生异常。", exception);
        }
    }

    private async Task BrowseOutputDirectoryAsync()
    {
        if (_filePickerService is null)
        {
            StatusMessage = "当前环境暂不支持打开目录选择器。";
            return;
        }

        try
        {
            var selectedFolder = await _filePickerService.PickFolderAsync("选择输出目录");
            if (string.IsNullOrWhiteSpace(selectedFolder))
            {
                StatusMessage = "已取消选择输出目录。";
                return;
            }

            switch (_selectedMergeMode)
            {
                case MergeWorkspaceMode.AudioJoin:
                    AudioJoinOutputDirectory = selectedFolder;
                    StatusMessage = $"已将音频拼接输出目录设置为：{AudioJoinOutputDirectory}";
                    break;
                case MergeWorkspaceMode.AudioVideoCompose:
                    AudioVideoComposeOutputDirectory = selectedFolder;
                    StatusMessage = $"已将音视频合成输出目录设置为：{AudioVideoComposeOutputDirectory}";
                    break;
                default:
                    OutputDirectory = selectedFolder;
                    StatusMessage = $"已将视频拼接输出目录设置为：{OutputDirectory}";
                    break;
            }
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "已取消选择输出目录。";
        }
        catch (Exception exception)
        {
            StatusMessage = "选择输出目录失败，请稍后重试。";
            _logger?.Log(LogLevel.Error, "选择合并输出目录时发生异常。", exception);
        }
    }

    private void ClearOutputDirectory()
    {
        if (!CanClearOutputDirectory())
        {
            return;
        }

        if (_selectedMergeMode == MergeWorkspaceMode.AudioJoin)
        {
            AudioJoinOutputDirectory = string.Empty;
            StatusMessage = "已清空音频拼接输出目录，处理时将恢复为当前音频基准素材所在文件夹。";
            return;
        }

        if (_selectedMergeMode == MergeWorkspaceMode.AudioVideoCompose)
        {
            AudioVideoComposeOutputDirectory = string.Empty;
            StatusMessage = "已清空音视频合成输出目录，处理时将恢复为当前视频素材所在文件夹。";
            return;
        }

        OutputDirectory = string.Empty;
        StatusMessage = "已清空视频拼接输出目录，处理时将恢复为分辨率预设视频所在文件夹输出。";
    }

    private Task StartProcessingAsync() => _selectedMergeMode switch
    {
        MergeWorkspaceMode.VideoJoin => StartVideoJoinProcessingAsync(),
        MergeWorkspaceMode.AudioJoin => StartAudioJoinProcessingAsync(),
        _ => StartAudioVideoComposeProcessingAsync()
    };

    private bool CanStartProcessing() => _selectedMergeMode switch
    {
        MergeWorkspaceMode.VideoJoin => CanStartVideoJoinProcessing(),
        MergeWorkspaceMode.AudioJoin => CanStartAudioJoinProcessing(),
        _ => CanStartAudioVideoComposeProcessing()
    };

    private void CancelProcessing() => CancelVideoJoinProcessing();

    private bool CanEditActiveOutputSettings() =>
        !IsVideoJoinProcessing &&
        (_selectedMergeMode == MergeWorkspaceMode.VideoJoin ||
         _selectedMergeMode == MergeWorkspaceMode.AudioJoin ||
         _selectedMergeMode == MergeWorkspaceMode.AudioVideoCompose);

    private async Task StartVideoJoinProcessingAsync()
    {
        if (!CanStartVideoJoinProcessing())
        {
            StatusMessage = GetVideoJoinCannotStartMessage();
            return;
        }

        if (_videoJoinWorkflowService is null || _mergeMediaAnalysisService is null)
        {
            StatusMessage = "当前环境暂不支持视频拼接输出。";
            return;
        }

        _videoJoinProcessingCancellationSource?.Dispose();
        _videoJoinProcessingCancellationSource = new CancellationTokenSource();
        var cancellationToken = _videoJoinProcessingCancellationSource.Token;

        try
        {
            IsVideoJoinProcessing = true;
            ShowProcessingPreparationProgress("视频拼接", "正在检查视频拼接素材并准备输出参数...");
            StatusMessage = "正在检查视频拼接素材并准备输出参数...";

            EnsureOutputDirectoryExists();

            var activeTrackItems = _videoJoinVideoTrackItems
                .Where(trackItem => trackItem.IsSourceAvailable)
                .ToArray();
            var presetTrackItem = GetEffectiveVideoResolutionPresetItem() ?? activeTrackItems[0];
            var presetTrackIndex = Array.FindIndex(activeTrackItems, trackItem => trackItem.TrackId == presetTrackItem.TrackId);
            if (presetTrackIndex < 0)
            {
                throw new InvalidOperationException("无法定位当前分辨率预设片段。");
            }

            var segments = await _mergeMediaAnalysisService.BuildVideoJoinSegmentsAsync(activeTrackItems, cancellationToken);
            var presetSegment = segments[presetTrackIndex];
            var plannedOutputPath = CreatePlannedVideoJoinOutputPath(presetTrackItem.SourcePath);
            var preferences = GetCurrentUserPreferences();
            var request = new VideoJoinExportRequest(
                segments,
                plannedOutputPath,
                SelectedOutputFormat,
                preferences.PreferredTranscodingMode,
                preferences.EnableGpuAccelerationForTranscoding,
                VideoAccelerationKind.None,
                presetSegment.Width,
                presetSegment.Height,
                presetSegment.FrameRate,
                _selectedSmallerResolutionStrategy,
                _selectedLargerResolutionStrategy);

            StatusMessage = "正在准备 FFmpeg 运行时...";
            await _videoJoinWorkflowService.EnsureRuntimeReadyAsync(cancellationToken);

            StatusMessage = $"FFmpeg 已就绪，正在拼接 {segments.Count} 段视频...";
            var progressReporter = new Progress<FFmpegProgressUpdate>(update => HandleVideoJoinProgress(update, segments.Count));
            var exportResult = await _videoJoinWorkflowService.ExportAsync(
                request,
                progressReporter,
                () => StatusMessage = "GPU 编码失败，已自动回退为 CPU 重试一次。",
                cancellationToken);
            var result = exportResult.ExecutionResult;

            if (result.WasSuccessful && File.Exists(exportResult.Request.OutputPath))
            {
                StatusMessage = AppendTranscodingMessage(
                    $"视频拼接完成：{Path.GetFileName(exportResult.Request.OutputPath)}。预设分辨率来源：{presetTrackItem.SourceName} · {presetTrackItem.ResolutionText}；较小分辨率视频：{GetSmallerResolutionStrategyLabel()}；较大分辨率视频：{GetLargerResolutionStrategyLabel()}。",
                    exportResult.TranscodingMessage);
                TryRevealVideoJoinOutputFile(exportResult.Request.OutputPath);
                return;
            }

            StatusMessage = result.WasCancelled
                ? "已取消视频拼接任务。"
                : AppendTranscodingMessage(
                    $"视频拼接失败：{ExtractFriendlyVideoJoinFailureMessage(result)}",
                    exportResult.TranscodingMessage);
        }
        catch (OperationCanceledException) when (_videoJoinProcessingCancellationSource?.IsCancellationRequested == true)
        {
            StatusMessage = "已取消视频拼接任务。";
        }
        catch (Exception exception)
        {
            StatusMessage = $"视频拼接任务执行失败：{exception.Message}";
            _logger?.Log(LogLevel.Error, "执行视频拼接任务时发生异常。", exception);
        }
        finally
        {
            ResetProcessingProgress();
            IsVideoJoinProcessing = false;
            _videoJoinProcessingCancellationSource?.Dispose();
            _videoJoinProcessingCancellationSource = null;
        }
    }

    private async Task StartAudioJoinProcessingAsync()
    {
        if (!CanStartAudioJoinProcessing())
        {
            StatusMessage = GetAudioJoinCannotStartMessage();
            return;
        }

        if (_audioJoinWorkflowService is null || _mergeMediaAnalysisService is null)
        {
            StatusMessage = "当前环境暂不支持音频拼接输出。";
            return;
        }

        _videoJoinProcessingCancellationSource?.Dispose();
        _videoJoinProcessingCancellationSource = new CancellationTokenSource();
        var cancellationToken = _videoJoinProcessingCancellationSource.Token;

        try
        {
            IsVideoJoinProcessing = true;
            ShowProcessingPreparationProgress("音频拼接", "正在检查音频拼接素材并准备输出参数...");
            StatusMessage = "正在检查音频拼接素材并准备输出参数...";

            EnsureAudioOutputDirectoryExists();

            var activeTrackItems = _audioJoinAudioTrackItems
                .Where(trackItem => trackItem.IsSourceAvailable)
                .ToArray();
            var segments = await _mergeMediaAnalysisService.BuildAudioJoinSegmentsAsync(activeTrackItems, cancellationToken);
            var outputAnchorTrackItem = activeTrackItems[0];
            TrackItem? presetTrackItem = null;
            var targetSampleRate = 0;
            int? targetBitrate = null;

            if (_selectedAudioJoinParameterMode == AudioJoinParameterMode.Preset)
            {
                presetTrackItem = GetEffectiveAudioParameterPresetItem() ?? activeTrackItems[0];
                var presetTrackIndex = Array.FindIndex(activeTrackItems, trackItem => trackItem.TrackId == presetTrackItem.TrackId);
                if (presetTrackIndex < 0)
                {
                    throw new InvalidOperationException("无法定位当前音频参数预设片段。");
                }

                var presetSegment = segments[presetTrackIndex];
                outputAnchorTrackItem = presetTrackItem;
                targetSampleRate = presetSegment.SampleRate;
                targetBitrate = presetSegment.Bitrate;
            }
            else
            {
                (targetSampleRate, targetBitrate) = ResolveBalancedAudioJoinTargets(segments);
            }

            var plannedOutputPath = CreatePlannedAudioJoinOutputPath(outputAnchorTrackItem.SourcePath);
            var preferences = GetCurrentUserPreferences();
            var request = new AudioJoinExportRequest(
                segments,
                plannedOutputPath,
                SelectedAudioJoinOutputFormat,
                preferences.PreferredTranscodingMode,
                preferences.EnableGpuAccelerationForTranscoding,
                _selectedAudioJoinParameterMode,
                targetSampleRate,
                targetBitrate);

            StatusMessage = "正在准备 FFmpeg 运行时...";
            await _audioJoinWorkflowService.EnsureRuntimeReadyAsync(cancellationToken);

            StatusMessage = $"FFmpeg 已就绪，正在拼接 {segments.Count} 段音频...";
            var progressReporter = new Progress<FFmpegProgressUpdate>(update => HandleAudioJoinProgress(update, segments.Count));
            var exportResult = await _audioJoinWorkflowService.ExportAsync(request, progressReporter, cancellationToken);
            var result = exportResult.ExecutionResult;

            if (result.WasSuccessful && File.Exists(exportResult.Request.OutputPath))
            {
                StatusMessage = AppendTranscodingMessage(
                    BuildAudioJoinCompletionMessage(exportResult.Request, presetTrackItem),
                    exportResult.TranscodingMessage);
                TryRevealVideoJoinOutputFile(exportResult.Request.OutputPath);
                return;
            }

            StatusMessage = result.WasCancelled
                ? "已取消音频拼接任务。"
                : AppendTranscodingMessage(
                    $"音频拼接失败：{ExtractFriendlyVideoJoinFailureMessage(result)}",
                    exportResult.TranscodingMessage);
        }
        catch (OperationCanceledException) when (_videoJoinProcessingCancellationSource?.IsCancellationRequested == true)
        {
            StatusMessage = "已取消音频拼接任务。";
        }
        catch (Exception exception)
        {
            StatusMessage = $"音频拼接任务执行失败：{exception.Message}";
            _logger?.Log(LogLevel.Error, "执行音频拼接任务时发生异常。", exception);
        }
        finally
        {
            ResetProcessingProgress();
            IsVideoJoinProcessing = false;
            _videoJoinProcessingCancellationSource?.Dispose();
            _videoJoinProcessingCancellationSource = null;
        }
    }

    private void CancelVideoJoinProcessing() => _videoJoinProcessingCancellationSource?.Cancel();

    private bool CanStartVideoJoinProcessing() =>
        _videoJoinWorkflowService is not null &&
        !IsVideoJoinProcessing &&
        _selectedMergeMode == MergeWorkspaceMode.VideoJoin &&
        _videoJoinVideoTrackItems.Any(trackItem => trackItem.IsSourceAvailable);

    private bool CanStartAudioJoinProcessing() =>
        _audioJoinWorkflowService is not null &&
        !IsVideoJoinProcessing &&
        _selectedMergeMode == MergeWorkspaceMode.AudioJoin &&
        _audioJoinAudioTrackItems.Any(trackItem => trackItem.IsSourceAvailable);

    private bool CanEditVideoJoinOutputSettings() =>
        !IsVideoJoinProcessing &&
        _selectedMergeMode == MergeWorkspaceMode.VideoJoin;

    private bool CanClearOutputDirectory() =>
        CanEditActiveOutputSettings() &&
        (_selectedMergeMode == MergeWorkspaceMode.AudioJoin
            ? AudioJoinHasCustomOutputDirectory
            : _selectedMergeMode == MergeWorkspaceMode.AudioVideoCompose
                ? AudioVideoComposeHasCustomOutputDirectory
                : HasCustomOutputDirectory);

    private string GetVideoJoinCannotStartMessage()
    {
        if (_selectedMergeMode != MergeWorkspaceMode.VideoJoin)
        {
            return "当前开始处理仅适用于视频拼接模式。";
        }

        if (IsVideoJoinProcessing)
        {
            return "视频拼接任务正在进行中。";
        }

        if (_videoJoinWorkflowService is null)
        {
            return "当前环境暂不支持视频拼接输出。";
        }

        return "请先向视频轨道添加至少一个有效视频片段。";
    }

    private string GetAudioJoinCannotStartMessage()
    {
        if (_selectedMergeMode != MergeWorkspaceMode.AudioJoin)
        {
            return "当前开始处理仅适用于音频拼接模式。";
        }

        if (IsVideoJoinProcessing)
        {
            return "音频拼接任务正在进行中。";
        }

        if (_audioJoinWorkflowService is null)
        {
            return "当前环境暂不支持音频拼接输出。";
        }

        return "请先向音频轨道添加至少一个有效音频片段。";
    }

    private static (int TargetSampleRate, int? TargetBitrate) ResolveBalancedAudioJoinTargets(
        IEnumerable<AudioJoinSegment> segments)
    {
        var targetSampleRate = ResolveDominantPositiveValue(
            segments.Select(segment => segment.SampleRate),
            fallbackValue: 48_000);
        var targetBitrate = ResolveDominantPositiveNullableValue(
            segments.Select(segment => segment.Bitrate));
        return (targetSampleRate, targetBitrate);
    }

    private static (int TargetSampleRate, int? TargetBitrate) ResolveBalancedAudioJoinTargets(
        IEnumerable<TrackItem> trackItems)
    {
        var availableTrackItems = trackItems
            .Where(trackItem => trackItem.IsSourceAvailable)
            .ToArray();

        var targetSampleRate = ResolveDominantPositiveValue(
            availableTrackItems
                .Select(trackItem => MergeMediaMetadataParser.TryResolveAudioJoinSampleRate(null, trackItem, out var sampleRate) ? sampleRate : 0),
            fallbackValue: 48_000);
        var targetBitrate = ResolveDominantPositiveNullableValue(
            availableTrackItems
                .Select(trackItem => MergeMediaMetadataParser.TryResolveAudioJoinBitrate(null, trackItem, out var bitrate) ? (int?)bitrate : null));
        return (targetSampleRate, targetBitrate);
    }

    private static int ResolveDominantPositiveValue(IEnumerable<int> values, int fallbackValue)
    {
        var dominantValue = values
            .Where(value => value > 0)
            .GroupBy(value => value)
            .OrderByDescending(group => group.Count())
            .ThenByDescending(group => group.Key)
            .Select(group => group.Key)
            .FirstOrDefault();

        return dominantValue > 0 ? dominantValue : fallbackValue;
    }

    private static int? ResolveDominantPositiveNullableValue(IEnumerable<int?> values)
    {
        var dominantValue = values
            .Where(value => value is > 0)
            .Select(value => value!.Value)
            .GroupBy(value => value)
            .OrderByDescending(group => group.Count())
            .ThenByDescending(group => group.Key)
            .Select(group => group.Key)
            .FirstOrDefault();

        return dominantValue > 0 ? dominantValue : null;
    }

    private void UpdateVideoJoinProgress(FFmpegProgressUpdate progress, int segmentCount)
    {
        if (progress.IsCompleted)
        {
            return;
        }

        if (progress.ProgressRatio is not double ratio)
        {
            StatusMessage = $"正在拼接 {segmentCount} 段视频，FFmpeg 正在返回实时进度...";
            return;
        }

        var normalized = Math.Clamp(ratio, 0d, 1d);
        var percentText = $"{Math.Round(normalized * 100d):0}%";
        StatusMessage = progress.ProcessedDuration is { } processedDuration && progress.TotalDuration is { } totalDuration
            ? $"正在拼接 {segmentCount} 段视频：{percentText}（{FormatDuration(processedDuration)} / {FormatDuration(totalDuration)}）"
            : $"正在拼接 {segmentCount} 段视频：{percentText}";
    }

    private void UpdateAudioJoinProgress(FFmpegProgressUpdate progress, int segmentCount)
    {
        if (progress.IsCompleted)
        {
            return;
        }

        if (progress.ProgressRatio is not double ratio)
        {
            StatusMessage = $"正在拼接 {segmentCount} 段音频，FFmpeg 正在返回实时进度...";
            return;
        }

        var normalized = Math.Clamp(ratio, 0d, 1d);
        var percentText = $"{Math.Round(normalized * 100d):0}%";
        StatusMessage = progress.ProcessedDuration is { } processedDuration && progress.TotalDuration is { } totalDuration
            ? $"正在拼接 {segmentCount} 段音频：{percentText}（{FormatDuration(processedDuration)} / {FormatDuration(totalDuration)}）"
            : $"正在拼接 {segmentCount} 段音频：{percentText}";
    }

    private string BuildAudioJoinCompletionMessage(AudioJoinExportRequest request, TrackItem? presetTrackItem)
    {
        var outputSummary = BuildAudioJoinOutputSummaryText(
            request.TargetSampleRate,
            request.TargetBitrate,
            request.OutputFormat,
            request.ParameterMode);

        return request.ParameterMode == AudioJoinParameterMode.Preset && presetTrackItem is not null
            ? $"音频拼接完成：{Path.GetFileName(request.OutputPath)}。参数预设来源：{presetTrackItem.SourceName} · {outputSummary}。"
            : $"音频拼接完成：{Path.GetFileName(request.OutputPath)}。当前模式：均衡模式 · {outputSummary}。";
    }

    private UserPreferences GetCurrentUserPreferences() =>
        _userPreferencesService?.Load() ?? new UserPreferences();

    private static string AppendTranscodingMessage(string baseMessage, string? transcodingMessage) =>
        string.IsNullOrWhiteSpace(transcodingMessage)
            ? baseMessage
            : $"{baseMessage} {transcodingMessage}";

    private static string FormatAudioJoinSampleRateText(int sampleRate)
    {
        if (sampleRate <= 0)
        {
            return "未知采样率";
        }

        return sampleRate >= 1_000
            ? $"{sampleRate / 1_000d:0.###} kHz"
            : $"{sampleRate:0} Hz";
    }

    private static string FormatAudioJoinBitrateText(int bitrate)
    {
        if (bitrate <= 0)
        {
            return "自动码率";
        }

        return bitrate >= 1_000_000
            ? $"{bitrate / 1_000_000d:0.##} Mbps"
            : $"{bitrate / 1_000d:0.##} kbps";
    }

    private static string BuildAudioJoinOutputSummaryText(
        int sampleRate,
        int? requestedBitrate,
        OutputFormatOption outputFormat,
        AudioJoinParameterMode parameterMode)
    {
        var sampleRateText = FormatAudioJoinSampleRateText(sampleRate);
        var bitrateText = ResolveAudioJoinOutputBitrateText(sampleRate, requestedBitrate, outputFormat, parameterMode);
        return $"{sampleRateText} · {bitrateText}";
    }

    private static string ResolveAudioJoinOutputBitrateText(
        int sampleRate,
        int? requestedBitrate,
        OutputFormatOption outputFormat,
        AudioJoinParameterMode parameterMode)
    {
        var effectiveBitrate = ResolveAudioJoinEffectiveBitrate(sampleRate, requestedBitrate, outputFormat, parameterMode);
        return effectiveBitrate is > 0
            ? FormatAudioJoinBitrateText(effectiveBitrate.Value)
            : $"{outputFormat.DisplayName} 编码器自动码率";
    }

    private static int? ResolveAudioJoinEffectiveBitrate(
        int sampleRate,
        int? requestedBitrate,
        OutputFormatOption outputFormat,
        AudioJoinParameterMode parameterMode)
    {
        return outputFormat.Extension.ToLowerInvariant() switch
        {
            ".mp3" => ResolveLossyAudioJoinBitrate(requestedBitrate, parameterMode, fallbackKbps: 192, minKbps: 96, maxKbps: 320),
            ".m4a" => ResolveLossyAudioJoinBitrate(requestedBitrate, parameterMode, fallbackKbps: 192, minKbps: 96, maxKbps: 320),
            ".aac" => ResolveLossyAudioJoinBitrate(requestedBitrate, parameterMode, fallbackKbps: 192, minKbps: 96, maxKbps: 320),
            ".wma" => ResolveLossyAudioJoinBitrate(requestedBitrate, parameterMode, fallbackKbps: 192, minKbps: 96, maxKbps: 320),
            ".ogg" => ResolveLossyAudioJoinBitrate(requestedBitrate, parameterMode, fallbackKbps: 192, minKbps: 96, maxKbps: 320),
            ".opus" => ResolveLossyAudioJoinBitrate(requestedBitrate, parameterMode, fallbackKbps: 160, minKbps: 48, maxKbps: 256),
            ".wav" => ResolveStereoPcm16Bitrate(sampleRate),
            ".aiff" => ResolveStereoPcm16Bitrate(sampleRate),
            ".aif" => ResolveStereoPcm16Bitrate(sampleRate),
            _ => null
        };
    }

    private static int ResolveLossyAudioJoinBitrate(
        int? requestedBitrate,
        AudioJoinParameterMode parameterMode,
        int fallbackKbps,
        int minKbps,
        int maxKbps)
    {
        if (requestedBitrate is not > 0)
        {
            return fallbackKbps * 1_000;
        }

        var rawKbps = (int)Math.Round(requestedBitrate.Value / 1_000d, MidpointRounding.AwayFromZero);
        var clampedKbps = Math.Clamp(rawKbps, minKbps, maxKbps);
        var effectiveKbps = parameterMode == AudioJoinParameterMode.Preset
            ? clampedKbps
            : Math.Max(32, (int)(Math.Round(clampedKbps / 16d, MidpointRounding.AwayFromZero) * 16d));
        return effectiveKbps * 1_000;
    }

    private static int ResolveStereoPcm16Bitrate(int sampleRate) =>
        sampleRate > 0 ? sampleRate * 2 * 16 : 0;

    private void TryRevealVideoJoinOutputFile(string outputPath)
    {
        if (_fileRevealService is null || _userPreferencesService is null)
        {
            return;
        }

        try
        {
            if (_userPreferencesService.Load().RevealOutputFileAfterProcessing)
            {
                _fileRevealService.RevealFile(outputPath);
            }
        }
        catch (Exception exception)
        {
            _logger?.Log(LogLevel.Warning, "视频拼接完成后定位输出文件失败。", exception);
        }
    }

    private static string ExtractFriendlyVideoJoinFailureMessage(FFmpegExecutionResult result)
    {
        if (result.WasCancelled)
        {
            return "当前任务已取消。";
        }

        if (result.TimedOut)
        {
            return "当前任务已超时。";
        }

        if (!string.IsNullOrWhiteSpace(result.FailureReason))
        {
            return result.FailureReason;
        }

        var diagnosticLine = result.StandardError
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .LastOrDefault();
        return !string.IsNullOrWhiteSpace(diagnosticLine)
            ? diagnosticLine.Trim()
            : result.ExitCode is int exitCode
                ? $"FFmpeg 已退出，返回代码：{exitCode}。"
                : "FFmpeg 未返回可读错误信息。";
    }

    private string CreatePlannedVideoJoinOutputPath(string presetSourcePath) =>
        MediaPathResolver.CreateUniqueOutputPath(
            MediaPathResolver.CreateMergeOutputPath(
                presetSourcePath,
                SelectedOutputFormat.Extension,
                HasCustomOutputDirectory ? OutputDirectory : null,
                GetEffectiveVideoJoinOutputBaseName(presetSourcePath)));

    private string CreatePlannedAudioJoinOutputPath(string presetSourcePath) =>
        MediaPathResolver.CreateUniqueOutputPath(
            MediaPathResolver.CreateMergeOutputPath(
                presetSourcePath,
                SelectedAudioJoinOutputFormat.Extension,
                AudioJoinHasCustomOutputDirectory ? AudioJoinOutputDirectory : null,
                GetEffectiveAudioJoinOutputBaseName(presetSourcePath)));

    private void EnsureOutputDirectoryExists()
    {
        if (HasCustomOutputDirectory)
        {
            Directory.CreateDirectory(OutputDirectory);
        }
    }

    private void EnsureAudioOutputDirectoryExists()
    {
        if (AudioJoinHasCustomOutputDirectory)
        {
            Directory.CreateDirectory(AudioJoinOutputDirectory);
        }
    }

    private void NotifyCommandStates()
    {
        _importFilesCommand.NotifyCanExecuteChanged();
        _browseOutputDirectoryCommand.NotifyCanExecuteChanged();
        _clearOutputDirectoryCommand.NotifyCanExecuteChanged();
        _startVideoJoinProcessingCommand.NotifyCanExecuteChanged();
        _cancelVideoJoinProcessingCommand.NotifyCanExecuteChanged();
    }

    private void SetSmallerResolutionStrategy(MergeSmallerResolutionStrategy strategy)
    {
        if (_selectedSmallerResolutionStrategy == strategy)
        {
            return;
        }

        _selectedSmallerResolutionStrategy = strategy;
        OnPropertyChanged(nameof(IsPadSmallerVideoWithBlackBarsSelected));
        OnPropertyChanged(nameof(IsStretchSmallerVideoToFillSelected));
        PersistVideoJoinPreferences();
        StatusMessage = strategy == MergeSmallerResolutionStrategy.PadWithBlackBars
            ? "较小分辨率视频将默认填充黑边。"
            : "较小分辨率视频将默认拉伸填充。";
    }

    private void SetLargerResolutionStrategy(MergeLargerResolutionStrategy strategy)
    {
        if (_selectedLargerResolutionStrategy == strategy)
        {
            return;
        }

        _selectedLargerResolutionStrategy = strategy;
        OnPropertyChanged(nameof(IsSqueezeLargerVideoToFitSelected));
        OnPropertyChanged(nameof(IsCropLargerVideoToFillSelected));
        PersistVideoJoinPreferences();
        StatusMessage = strategy == MergeLargerResolutionStrategy.SqueezeToFit
            ? "较大分辨率视频将默认挤压到预设分辨率。"
            : "较大分辨率视频将默认裁剪到预设分辨率。";
    }

    private OutputFormatOption ResolvePreferredVideoJoinOutputFormat(string? extension)
    {
        var normalizedExtension = string.IsNullOrWhiteSpace(extension)
            ? null
            : (extension.StartsWith(".", StringComparison.Ordinal) ? extension : $".{extension}");

        var preferredFormat = normalizedExtension is null
            ? null
            : _videoJoinOutputFormats.FirstOrDefault(
                format => string.Equals(format.Extension, normalizedExtension, StringComparison.OrdinalIgnoreCase));

        return preferredFormat ?? _videoJoinOutputFormats.First();
    }

    private OutputFormatOption ResolvePreferredAudioJoinOutputFormat(string? extension)
    {
        var normalizedExtension = string.IsNullOrWhiteSpace(extension)
            ? null
            : (extension.StartsWith(".", StringComparison.Ordinal) ? extension : $".{extension}");

        var preferredFormat = normalizedExtension is null
            ? null
            : _audioJoinOutputFormats.FirstOrDefault(
                format => string.Equals(format.Extension, normalizedExtension, StringComparison.OrdinalIgnoreCase));

        return preferredFormat ?? _audioJoinOutputFormats.First();
    }

    private void PersistVideoJoinPreferences()
    {
        if (_userPreferencesService is null)
        {
            return;
        }

        _userPreferencesService.Update(existingPreferences => existingPreferences with
        {
            PreferredMergeVideoJoinOutputFormatExtension = _selectedVideoJoinOutputFormat?.Extension,
            PreferredMergeVideoJoinOutputDirectory = HasCustomOutputDirectory ? OutputDirectory : null,
            PreferredMergeSmallerResolutionStrategy = _selectedSmallerResolutionStrategy,
            PreferredMergeLargerResolutionStrategy = _selectedLargerResolutionStrategy
        });
    }

    private void PersistAudioJoinPreferences()
    {
        if (_userPreferencesService is null)
        {
            return;
        }

        _userPreferencesService.Update(existingPreferences => existingPreferences with
        {
            PreferredMergeAudioJoinOutputFormatExtension = _selectedAudioJoinOutputFormat?.Extension,
            PreferredMergeAudioJoinOutputDirectory = AudioJoinHasCustomOutputDirectory ? AudioJoinOutputDirectory : null,
            PreferredMergeAudioJoinParameterMode = _selectedAudioJoinParameterMode
        });
    }

    private static AudioJoinParameterMode ResolvePreferredAudioJoinParameterMode(AudioJoinParameterMode preferredMode) =>
        Enum.IsDefined(typeof(AudioJoinParameterMode), preferredMode)
            ? preferredMode
            : AudioJoinParameterMode.Balanced;

    private static MergeSmallerResolutionStrategy ResolvePreferredMergeSmallerResolutionStrategy(
        MergeSmallerResolutionStrategy preferredStrategy) =>
        Enum.IsDefined(typeof(MergeSmallerResolutionStrategy), preferredStrategy)
            ? preferredStrategy
            : MergeSmallerResolutionStrategy.PadWithBlackBars;

    private static MergeLargerResolutionStrategy ResolvePreferredMergeLargerResolutionStrategy(
        MergeLargerResolutionStrategy preferredStrategy) =>
        Enum.IsDefined(typeof(MergeLargerResolutionStrategy), preferredStrategy)
            ? preferredStrategy
            : MergeLargerResolutionStrategy.SqueezeToFit;

    private string NormalizeOutputDirectory(string? outputDirectory)
    {
        if (MediaPathResolver.TryNormalizeOutputDirectory(outputDirectory, out var normalizedDirectory))
        {
            return normalizedDirectory;
        }

        _logger?.Log(LogLevel.Warning, "检测到无效的合并输出目录配置，已回退为预设视频原文件夹输出。");
        return string.Empty;
    }

    private string GetSmallerResolutionStrategyLabel() =>
        _selectedSmallerResolutionStrategy == MergeSmallerResolutionStrategy.PadWithBlackBars
            ? "填充黑边"
            : "拉伸填充";

    private string GetLargerResolutionStrategyLabel() =>
        _selectedLargerResolutionStrategy == MergeLargerResolutionStrategy.SqueezeToFit
            ? "挤压"
            : "裁剪";

    private void SetAudioJoinParameterMode(AudioJoinParameterMode parameterMode)
    {
        if (_selectedAudioJoinParameterMode == parameterMode)
        {
            return;
        }

        if (IsVideoJoinProcessing)
        {
            StatusMessage = "音频拼接任务处理中，若需切换参数模式，请先取消当前任务。";
            OnPropertyChanged(nameof(IsBalancedAudioJoinParameterModeSelected));
            OnPropertyChanged(nameof(IsPresetAudioJoinParameterModeSelected));
            return;
        }

        _selectedAudioJoinParameterMode = parameterMode;
        OnPropertyChanged(nameof(IsBalancedAudioJoinParameterModeSelected));
        OnPropertyChanged(nameof(IsPresetAudioJoinParameterModeSelected));
        OnPropertyChanged(nameof(AudioJoinPresetSelectionVisibility));
        OnPropertyChanged(nameof(AudioTrackOperationHintText));
        OnPropertyChanged(nameof(AudioJoinParameterModeHintText));
        RefreshTrackCollection(_audioJoinAudioTrackItems, supportsVideoPreset: false);
        RaiseTrackStatePropertiesChanged();
        PersistAudioJoinPreferences();
        StatusMessage = parameterMode == AudioJoinParameterMode.Preset
            ? "已切换到指定预设模式，可在音频轨道上选择一段音频作为参数预设。"
            : "已切换到均衡模式，系统将按全部有效音轨自动统一参数。";
    }

    private string GetEffectiveVideoJoinOutputBaseName() =>
        GetEffectiveVideoJoinOutputBaseName(GetEffectiveVideoResolutionPresetItem()?.SourcePath);

    private string GetEffectiveVideoJoinOutputBaseName(string? presetSourcePath)
    {
        if (!string.IsNullOrWhiteSpace(VideoJoinOutputFileName))
        {
            return VideoJoinOutputFileName;
        }

        var presetFileNameWithoutExtension = string.IsNullOrWhiteSpace(presetSourcePath)
            ? "merged_video"
            : Path.GetFileNameWithoutExtension(presetSourcePath);
        return $"{presetFileNameWithoutExtension}_merged";
    }

    private static string NormalizeVideoJoinOutputFileName(string? outputFileName)
    {
        if (string.IsNullOrWhiteSpace(outputFileName))
        {
            return string.Empty;
        }

        return MediaPathResolver.SanitizeOutputFileName(outputFileName);
    }

    private static string NormalizeOutputFileName(string? outputFileName) =>
        NormalizeVideoJoinOutputFileName(outputFileName);

    private string? GetDefaultVideoJoinOutputDirectory()
    {
        var presetTrackItem = GetEffectiveVideoResolutionPresetItem();
        if (presetTrackItem is null || string.IsNullOrWhiteSpace(presetTrackItem.SourcePath))
        {
            return null;
        }

        try
        {
            return Path.GetDirectoryName(NormalizeSourcePath(presetTrackItem.SourcePath));
        }
        catch (Exception exception)
        {
            _logger?.Log(LogLevel.Warning, $"解析视频拼接默认输出目录失败：{presetTrackItem.SourcePath}", exception);
            return null;
        }
    }

    private string GetEffectiveAudioJoinOutputBaseName() =>
        GetEffectiveAudioJoinOutputBaseName(GetAudioJoinOutputAnchorTrackItem()?.SourcePath);

    private string GetEffectiveAudioJoinOutputBaseName(string? presetSourcePath)
    {
        if (!string.IsNullOrWhiteSpace(AudioJoinOutputFileName))
        {
            return AudioJoinOutputFileName;
        }

        var presetFileNameWithoutExtension = string.IsNullOrWhiteSpace(presetSourcePath)
            ? "merged_audio"
            : Path.GetFileNameWithoutExtension(presetSourcePath);
        return $"{presetFileNameWithoutExtension}_merged";
    }

    private string? GetDefaultAudioJoinOutputDirectory()
    {
        var anchorTrackItem = GetAudioJoinOutputAnchorTrackItem();
        if (anchorTrackItem is null || string.IsNullOrWhiteSpace(anchorTrackItem.SourcePath))
        {
            return null;
        }

        try
        {
            return Path.GetDirectoryName(NormalizeSourcePath(anchorTrackItem.SourcePath));
        }
        catch (Exception exception)
        {
            _logger?.Log(LogLevel.Warning, $"解析音频拼接默认输出目录失败：{anchorTrackItem.SourcePath}", exception);
            return null;
        }
    }

    private TrackItem? GetAudioJoinOutputAnchorTrackItem() =>
        _selectedAudioJoinParameterMode == AudioJoinParameterMode.Preset
            ? GetEffectiveAudioParameterPresetItem()
            : _audioJoinAudioTrackItems.FirstOrDefault(trackItem => trackItem.IsSourceAvailable);

    private async Task<MediaItem> CreateMediaItemAsync(string filePath)
    {
        var fileName = Path.GetFileName(filePath);
        var isVideo = IsVideoByExtension(filePath);
        var durationText = "未知时长";
        var durationSeconds = 0;
        var resolutionText = isVideo ? "未知分辨率" : "未知音频参数";

        if (_mediaInfoService is null)
        {
            return new MediaItem(fileName, durationText, durationSeconds, isVideo, filePath, resolutionText);
        }

        try
        {
            var loadResult = _mediaInfoService.TryGetCachedDetails(filePath, out var cachedSnapshot)
                ? MediaDetailsLoadResult.Success(cachedSnapshot)
                : await _mediaInfoService.GetMediaDetailsAsync(filePath);

            if (loadResult.IsSuccess && loadResult.Snapshot is not null)
            {
                var snapshot = loadResult.Snapshot;
                fileName = string.IsNullOrWhiteSpace(snapshot.FileName) ? fileName : snapshot.FileName;
                isVideo = ResolveIsVideo(snapshot, filePath);
                resolutionText = isVideo
                    ? MergeMediaMetadataParser.ResolveResolutionText(snapshot)
                    : MergeMediaMetadataParser.ResolveAudioParameterText(snapshot);

                if (snapshot.MediaDuration is { } mediaDuration && mediaDuration > TimeSpan.Zero)
                {
                    durationText = FormatDuration(mediaDuration);
                    durationSeconds = Math.Max(0, (int)Math.Ceiling(mediaDuration.TotalSeconds));
                }
            }
            else if (!string.IsNullOrWhiteSpace(loadResult.ErrorMessage))
            {
                _logger?.Log(LogLevel.Warning, $"读取素材信息失败：{fileName}，已按基础信息导入。");
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger?.Log(LogLevel.Warning, $"读取素材信息失败：{fileName}，已按基础信息导入。", exception);
        }

        return new MediaItem(fileName, durationText, durationSeconds, isVideo, filePath, resolutionText);
    }

    private bool IsVideoByExtension(string filePath)
    {
        var extension = Path.GetExtension(filePath);
        if (string.IsNullOrWhiteSpace(extension))
        {
            return true;
        }

        return _supportedVideoInputFileTypes.Contains(extension) || !_supportedAudioInputFileTypes.Contains(extension);
    }

    private bool ResolveIsVideo(MediaDetailsSnapshot snapshot, string filePath)
    {
        if (snapshot.HasVideoStream)
        {
            return true;
        }

        if (snapshot.HasAudioStream)
        {
            return false;
        }

        return IsVideoByExtension(filePath);
    }

    private static string FormatDuration(TimeSpan duration) => duration.ToString(@"hh\:mm\:ss");

    private static bool TryParseTrackDuration(string durationText, out TimeSpan duration) =>
        TimeSpan.TryParse(durationText, out duration);

    private TrackItem CreateTrackItem(MediaItem mediaItem, int index, bool isSourceAvailable)
    {
        ArgumentNullException.ThrowIfNull(mediaItem);
        var visualWidth = mediaItem.IsVideo
            ? Math.Clamp(164d + (mediaItem.DurationSeconds * 2.2d), 248d, 360d)
            : Math.Clamp(148d + (mediaItem.DurationSeconds * 1.8d), 220d, 320d);

        return new TrackItem(
            mediaItem.FileName,
            mediaItem.SourcePath,
            mediaItem.DurationText,
            mediaItem.DurationSeconds,
            mediaItem.ResolutionText,
            visualWidth,
            mediaItem.IsVideo,
            index,
            isSourceAvailable,
            ResolveMediaItemHasEmbeddedAudio(mediaItem));
    }

    private bool ResolveMediaItemHasEmbeddedAudio(MediaItem mediaItem)
    {
        ArgumentNullException.ThrowIfNull(mediaItem);
        if (!mediaItem.IsVideo ||
            _mediaInfoService is null ||
            string.IsNullOrWhiteSpace(mediaItem.SourcePath))
        {
            return false;
        }

        return _mediaInfoService.TryGetCachedDetails(mediaItem.SourcePath, out var snapshot) &&
               snapshot.HasAudioStream;
    }

    private void SetMergeMode(MergeWorkspaceMode mergeMode)
    {
        if (_selectedMergeMode == mergeMode)
        {
            return;
        }

        if (IsVideoJoinProcessing)
        {
            StatusMessage = "当前合并任务处理中，若需切换模式，请先取消当前任务。";
            OnPropertyChanged(nameof(IsVideoJoinModeSelected));
            OnPropertyChanged(nameof(IsAudioJoinModeSelected));
            OnPropertyChanged(nameof(IsAudioVideoComposeModeSelected));
            return;
        }

        _selectedMergeMode = mergeMode;
        OnPropertyChanged(nameof(IsVideoJoinModeSelected));
        OnPropertyChanged(nameof(IsAudioJoinModeSelected));
        OnPropertyChanged(nameof(IsAudioVideoComposeModeSelected));
        OnPropertyChanged(nameof(VideoJoinOutputSettingsVisibility));
        OnPropertyChanged(nameof(AudioJoinOutputSettingsVisibility));
        OnPropertyChanged(nameof(NonVideoJoinOutputSettingsVisibility));
        OnPropertyChanged(nameof(AudioVideoComposeOutputSettingsVisibility));
        OnPropertyChanged(nameof(TimelineHintText));
        OnPropertyChanged(nameof(VideoTrackItems));
        OnPropertyChanged(nameof(AudioTrackItems));
        OnPropertyChanged(nameof(VideoTrackEmptyText));
        OnPropertyChanged(nameof(AudioTrackEmptyText));
        OnPropertyChanged(nameof(VideoJoinTimelineVisibility));
        OnPropertyChanged(nameof(AudioJoinTimelineVisibility));
        OnPropertyChanged(nameof(StandardTimelineVisibility));
        RaiseTrackStatePropertiesChanged();
        SetModeMismatchWarningVisibility(false, string.Empty);
        NotifyCommandStates();

        StatusMessage = GetModeState(mergeMode).Profile.SelectionMessage;

        PersistMergeWorkspaceMode();
    }

    private MergeWorkspaceMode ResolvePreferredMergeMode(MergeWorkspaceMode preferredMode) =>
        Enum.IsDefined(typeof(MergeWorkspaceMode), preferredMode)
            ? preferredMode
            : MergeWorkspaceMode.AudioVideoCompose;

    private void PersistMergeWorkspaceMode()
    {
        if (_userPreferencesService is null)
        {
            return;
        }

        _userPreferencesService.Update(existingPreferences => existingPreferences with
        {
            PreferredMergeWorkspaceMode = _selectedMergeMode
        });
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

    private void SetModeMismatchWarningVisibility(bool isVisible, string? message = null)
    {
        if (message is not null && _modeMismatchWarningMessage != message)
        {
            _modeMismatchWarningMessage = message;
            OnPropertyChanged(nameof(ModeMismatchWarningMessage));
        }

        if (_isModeMismatchWarningVisible != isVisible)
        {
            _isModeMismatchWarningVisible = isVisible;
            OnPropertyChanged(nameof(ModeMismatchWarningVisibility));
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
        var profiles = configuration.MergeModeProfiles;

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

    private static string BuildInvalidTrackItemsMessage(int invalidatedCount)
    {
        var countText = invalidatedCount == 1 ? "1 个轨道片段" : $"{invalidatedCount} 个轨道片段";
        return $"检测到 {countText} 引用的源素材已从素材列表中移除。相关片段已标记为失效，将不会参与当前合并输出。请重新添加源素材，或直接从轨道中移除这些失效片段。";
    }
}
