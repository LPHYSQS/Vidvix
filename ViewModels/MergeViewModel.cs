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
using Vidvix.Services;
using Vidvix.Utils;

namespace Vidvix.ViewModels;

public sealed partial class MergeViewModel : ObservableObject
{
    private readonly ApplicationConfiguration _configuration;
    private readonly ObservableCollection<MediaItem> _mediaItems;
    private readonly ObservableCollection<TrackItem> _videoJoinVideoTrackItems;
    private readonly ObservableCollection<TrackItem> _audioJoinAudioTrackItems;
    private readonly ObservableCollection<TrackItem> _audioVideoComposeVideoTrackItems;
    private readonly ObservableCollection<TrackItem> _audioVideoComposeAudioTrackItems;
    private readonly ObservableCollection<TrackItem> _emptyTrackItems;
    private IReadOnlyDictionary<MergeWorkspaceMode, MergeWorkspaceModeState> _modeStates;
    private IReadOnlyList<OutputFormatOption> _videoJoinOutputFormats;
    private IReadOnlyList<OutputFormatOption> _audioJoinOutputFormats;
    private readonly AsyncRelayCommand _importFilesCommand;
    private readonly AsyncRelayCommand _browseOutputDirectoryCommand;
    private readonly RelayCommand _clearOutputDirectoryCommand;
    private readonly AsyncRelayCommand _startVideoJoinProcessingCommand;
    private readonly RelayCommand _cancelVideoJoinProcessingCommand;
    private readonly IFilePickerService? _filePickerService;
    private readonly IMediaImportDiscoveryService? _mediaImportDiscoveryService;
    private readonly IMergeMediaAnalysisService? _mergeMediaAnalysisService;
    private readonly IVideoJoinWorkflowService? _videoJoinWorkflowService;
    private readonly IAudioJoinWorkflowService? _audioJoinWorkflowService;
    private readonly IFileRevealService? _fileRevealService;
    private readonly IMediaInfoService? _mediaInfoService;
    private readonly ILocalizationService? _localizationService;
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
    private LocalizedTextState? _statusMessageState;
    private LocalizedTextState? _modeMismatchWarningMessageState;
    private bool _isModeMismatchWarningVisible;
    private bool _isVideoJoinProcessing;
    private MergeWorkspaceMode _selectedMergeMode;
    private MergeSmallerResolutionStrategy _selectedSmallerResolutionStrategy;
    private MergeLargerResolutionStrategy _selectedLargerResolutionStrategy;
    private Guid? _manualVideoResolutionPresetTrackId;
    private Guid? _manualAudioParameterPresetTrackId;
    private AudioJoinParameterMode _selectedAudioJoinParameterMode;

    internal MergeViewModel(MergeWorkspaceDependencies? dependencies = null)
    {
        dependencies ??= new MergeWorkspaceDependencies();

        var effectiveConfiguration = dependencies.Configuration ?? new ApplicationConfiguration();
        _configuration = effectiveConfiguration;
        var preferences = dependencies.UserPreferencesService?.Load() ?? new UserPreferences();

        _filePickerService = dependencies.FilePickerService;
        _localizationService = dependencies.LocalizationService;
        _mediaImportDiscoveryService = dependencies.MediaImportDiscoveryService;
        _mergeMediaAnalysisService = dependencies.MergeMediaAnalysisService;
        _videoJoinWorkflowService = dependencies.VideoJoinWorkflowService;
        _audioJoinWorkflowService = dependencies.AudioJoinWorkflowService;
        _audioVideoComposeWorkflowService = dependencies.AudioVideoComposeWorkflowService;
        _fileRevealService = dependencies.FileRevealService;
        _mediaInfoService = dependencies.MediaInfoService;
        _userPreferencesService = dependencies.UserPreferencesService;
        _logger = dependencies.Logger;
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
        _videoJoinOutputFormats = BuildVideoJoinOutputFormats();
        _audioJoinOutputFormats = BuildAudioJoinOutputFormats();
        _modeStates = CreateModeStates(effectiveConfiguration);

        _selectedVideoJoinOutputFormat = ResolvePreferredVideoJoinOutputFormat(preferences.PreferredMergeVideoJoinOutputFormatExtension);
        _selectedAudioJoinOutputFormat = ResolvePreferredAudioJoinOutputFormat(preferences.PreferredMergeAudioJoinOutputFormatExtension);
        _videoJoinOutputDirectory = NormalizeOutputDirectory(preferences.PreferredMergeVideoJoinOutputDirectory);
        _audioJoinOutputDirectory = NormalizeOutputDirectory(preferences.PreferredMergeAudioJoinOutputDirectory);
        _videoJoinOutputFileName = string.Empty;
        _audioJoinOutputFileName = string.Empty;
        _statusMessage = string.Empty;
        _modeMismatchWarningMessage = string.Empty;
        _selectedMergeMode = ResolvePreferredMergeMode(preferences.PreferredMergeWorkspaceMode);
        _selectedSmallerResolutionStrategy = ResolvePreferredMergeSmallerResolutionStrategy(preferences.PreferredMergeSmallerResolutionStrategy);
        _selectedLargerResolutionStrategy = ResolvePreferredMergeLargerResolutionStrategy(preferences.PreferredMergeLargerResolutionStrategy);
        _selectedAudioJoinParameterMode = ResolvePreferredAudioJoinParameterMode(preferences.PreferredMergeAudioJoinParameterMode);
        InitializeAudioVideoComposeState(preferences);
        SetStatusMessage(
            "merge.status.initial",
            "请先导入视频或音频素材，再将它们添加到对应轨道。");

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
                SetStatusMessage(
                    "merge.status.videoJoin.outputFormat.changed",
                    "视频拼接输出格式已切换为 {format}。",
                    LocalizedArgument("format", () => SelectedOutputFormat.DisplayName));
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

    public string OutputDirectoryHintText =>
        GetLocalizedText(
            "merge.summary.outputDirectoryHint.videoJoin",
            "默认使用当前模式下基准素材所在文件夹；设置后，处理结果会统一输出到所选文件夹。");

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
        FormatLocalizedText(
            string.IsNullOrWhiteSpace(VideoJoinOutputFileName)
                ? "merge.summary.outputNameHint.default"
                : "merge.summary.outputNameHint.custom",
            string.IsNullOrWhiteSpace(VideoJoinOutputFileName)
                ? "留空时默认使用 {fileName}；若目标目录中已存在同名文件，系统会自动追加序号，避免覆盖原始文件。"
                : "当前将输出为 {fileName}；若目标目录中已存在同名文件，系统会自动追加序号，避免覆盖原始文件。",
            ("fileName", VideoJoinResolvedOutputFileName));

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
                SetStatusMessage(
                    "merge.status.audioJoin.outputFormat.changed",
                    "音频拼接输出格式已切换为 {format}。",
                    LocalizedArgument("format", () => SelectedAudioJoinOutputFormat.DisplayName));
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
        FormatLocalizedText(
            string.IsNullOrWhiteSpace(AudioJoinOutputFileName)
                ? "merge.summary.outputNameHint.default"
                : "merge.summary.outputNameHint.custom",
            string.IsNullOrWhiteSpace(AudioJoinOutputFileName)
                ? "留空时默认使用 {fileName}；若目标目录中已存在同名文件，系统会自动追加序号，避免覆盖原始文件。"
                : "当前将输出为 {fileName}；若目标目录中已存在同名文件，系统会自动追加序号，避免覆盖原始文件。",
            ("fileName", AudioJoinResolvedOutputFileName));

    public string StatusMessage
    {
        get => _statusMessage;
        private set
        {
            ClearStatusMessageLocalizationState();
            SetProperty(ref _statusMessage, value);
        }
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
            ? GetLocalizedText(
                "merge.summary.audioTrackOperationHint.preset",
                "可左键长按拖拽片段调整顺序，也可使用片段顶部按钮快速移除或设为参数预设。")
            : GetLocalizedText(
                "merge.summary.audioTrackOperationHint.balanced",
                "可左键长按拖拽片段调整顺序，也可使用片段顶部按钮快速移除。均衡模式不支持手动参数预设。");

    public string AudioJoinParameterModeHintText =>
        _selectedAudioJoinParameterMode == AudioJoinParameterMode.Preset
            ? FormatLocalizedText(
                "merge.summary.audioParameterModeHint.preset",
                "指定预设模式会锁定当前参数预设片段的采样率与目标码率：低于目标的音频会补齐到目标，高于目标的会压到目标，匹配的保持不动。若 {format} 本身不支持固定码率，系统会优先锁定采样率并尽可能贴近目标码率。",
                ("format", SelectedAudioJoinOutputFormat.DisplayName))
            : FormatLocalizedText(
                "merge.summary.audioParameterModeHint.balanced",
                "均衡模式不会锁定某一个预设片段。系统会按全部有效音轨中最常见的采样率与码率统一处理，再根据 {format} 的编码规则做兼容性量化，因此重新导入后看到的 kHz / kbps 可能不会与某条原始音频完全一致。",
                ("format", SelectedAudioJoinOutputFormat.DisplayName));

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
        ? GetLocalizedText("merge.summary.trackCount.empty", "未添加片段")
        : FormatLocalizedText(
            "merge.summary.trackCount.value",
            "{count} 个片段",
            ("count", ActiveVideoTrackItems.Count));

    public string VideoJoinTotalDurationText
    {
        get
        {
            var activeTrackItems = _videoJoinVideoTrackItems
                .Where(trackItem => trackItem.IsSourceAvailable)
                .ToArray();
            if (activeTrackItems.Length == 0)
            {
                return GetLocalizedText("merge.summary.totalDuration.empty", "总时长 · 00:00:00");
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
            return FormatLocalizedText(
                "merge.summary.totalDuration.value",
                "总时长 · {duration}{suffix}",
                ("duration", FormatDuration(totalDuration)),
                ("suffix", suffix));
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
                return GetLocalizedText("merge.summary.totalDuration.empty", "总时长 · 00:00:00");
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
            return FormatLocalizedText(
                "merge.summary.totalDuration.value",
                "总时长 · {duration}{suffix}",
                ("duration", FormatDuration(totalDuration)),
                ("suffix", suffix));
        }
    }

    public string AudioTrackSummaryText => ActiveAudioTrackItems.Count == 0
        ? GetLocalizedText("merge.summary.trackCount.empty", "未添加片段")
        : FormatLocalizedText(
            "merge.summary.trackCount.value",
            "{count} 个片段",
            ("count", ActiveAudioTrackItems.Count));

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
                return GetLocalizedText(
                    "merge.summary.videoResolutionPreset.empty",
                    "添加视频片段后，将默认以首个可用视频作为分辨率预设。");
            }

            var presetTrackItem = GetEffectiveVideoResolutionPresetItem();
            if (presetTrackItem is null)
            {
                return GetLocalizedText(
                    "merge.summary.videoResolutionPreset.unavailable",
                    "当前暂无可用的视频片段可作为分辨率预设。");
            }

            return HasManualVideoResolutionPresetSelection()
                ? FormatLocalizedText(
                    "merge.summary.videoResolutionPreset.manual",
                    "当前分辨率预设：{name}",
                    ("name", presetTrackItem.SourceName))
                : FormatLocalizedText(
                    "merge.summary.videoResolutionPreset.auto",
                    "当前默认以首个可用视频作为分辨率预设：{name}",
                    ("name", presetTrackItem.SourceName));
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
                    return GetLocalizedText(
                        "merge.summary.audioParameterPreset.balanced.empty",
                        "当前为均衡模式，添加音频片段后会按全部有效音轨自动统一参数。");
                }

                var (targetSampleRate, targetBitrate) = ResolveBalancedAudioJoinTargets(_audioJoinAudioTrackItems);
                return FormatLocalizedText(
                    "merge.summary.audioParameterPreset.balanced.value",
                    "当前为均衡模式：不使用手动参数预设，预计输出 {summary}。",
                    ("summary", BuildAudioJoinOutputSummaryText(
                        targetSampleRate,
                        targetBitrate,
                        SelectedAudioJoinOutputFormat,
                        AudioJoinParameterMode.Balanced)));
            }

            if (_audioJoinAudioTrackItems.Count == 0)
            {
                return GetLocalizedText(
                    "merge.summary.audioParameterPreset.preset.empty",
                    "添加音频片段后，将默认以首个可用音频作为参数预设。");
            }

            var presetTrackItem = GetEffectiveAudioParameterPresetItem();
            if (presetTrackItem is null)
            {
                return GetLocalizedText(
                    "merge.summary.audioParameterPreset.preset.unavailable",
                    "当前暂无可用的音频片段可作为参数预设。");
            }

            var sampleRate = MergeMediaMetadataParser.TryResolveAudioJoinSampleRate(null, presetTrackItem, out var resolvedSampleRate)
                ? resolvedSampleRate
                : 0;
            int? bitrate = MergeMediaMetadataParser.TryResolveAudioJoinBitrate(null, presetTrackItem, out var resolvedBitrate)
                ? resolvedBitrate
                : null;

            return HasManualAudioParameterPresetSelection()
                ? FormatLocalizedText(
                    "merge.summary.audioParameterPreset.preset.manual",
                    "当前音频参数预设：{name} · 目标输出 {summary}。",
                    ("name", presetTrackItem.SourceName),
                    ("summary", BuildAudioJoinOutputSummaryText(
                        sampleRate,
                        bitrate,
                        SelectedAudioJoinOutputFormat,
                        AudioJoinParameterMode.Preset)))
                : FormatLocalizedText(
                    "merge.summary.audioParameterPreset.preset.auto",
                    "当前默认以首个可用音频作为参数预设：{name} · 目标输出 {summary}。",
                    ("name", presetTrackItem.SourceName),
                    ("summary", BuildAudioJoinOutputSummaryText(
                        sampleRate,
                        bitrate,
                        SelectedAudioJoinOutputFormat,
                        AudioJoinParameterMode.Preset)));
        }
    }

    public string VideoTrackEmptyText => CurrentModeState.Profile.VideoTrackEmptyText;

    public string AudioTrackEmptyText => CurrentModeState.Profile.AudioTrackEmptyText;

    public void AddMediaToTimeline(MediaItem mediaItem)
    {
        ArgumentNullException.ThrowIfNull(mediaItem);

        if (IsVideoJoinProcessing)
        {
            SetProcessingLockedStatusMessage(
                "merge.label.module.merge",
                "合并",
                "merge.label.operation.adjustTrack",
                "调整轨道");
            return;
        }

        if (!TryResolveTrackCollectionForAddition(mediaItem, out var trackItems, out var rejectionMessage))
        {
            var rejectionKeySuffix = mediaItem.IsVideo ? "rejectVideoInputMessage" : "rejectAudioInputMessage";
            SetModeMismatchWarningVisibility(
                true,
                $"{CurrentModeState.Profile.LocalizationKeyPrefix}.{rejectionKeySuffix}",
                rejectionMessage);
            SetStatusMessage(
                $"{CurrentModeState.Profile.LocalizationKeyPrefix}.{rejectionKeySuffix}",
                rejectionMessage);
            return;
        }

        ClearModeMismatchWarning();
        if ((mediaItem.IsVideo && CurrentModeState.Profile.ReplaceVideoTrackOnAdd) ||
            (mediaItem.IsAudio && CurrentModeState.Profile.ReplaceAudioTrackOnAdd))
        {
            AddMediaToAudioVideoComposeTimeline(mediaItem, trackItems);
            return;
        }

        trackItems.Add(CreateTrackItem(mediaItem, trackItems.Count + 1, IsSourcePathAvailable(mediaItem.SourcePath)));
        SetStatusMessage(
            mediaItem.IsVideo ? "merge.status.trackAdded.video" : "merge.status.trackAdded.audio",
            mediaItem.IsVideo ? "{fileName} 已加入视频轨道。" : "{fileName} 已加入音频轨道。",
            ("fileName", mediaItem.FileName));
    }

    public void RemoveMediaItem(MediaItem mediaItem)
    {
        ArgumentNullException.ThrowIfNull(mediaItem);

        if (IsVideoJoinProcessing)
        {
            SetProcessingLockedStatusMessage(
                "merge.label.module.merge",
                "合并",
                "merge.label.operation.adjustMedia",
                "调整素材");
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
            SetStatusMessage(
                "merge.status.mediaRemoved",
                "已从素材列表移除 {fileName}。",
                ("fileName", mediaItem.FileName));
            return;
        }

        var notificationMessage = BuildInvalidTrackItemsMessage(invalidatedTrackItems.Length);
        SetStatusMessage(
            "merge.status.invalidTrackItems.message",
            notificationMessage,
            ("count", invalidatedTrackItems.Length));
        InvalidTrackItemsDetected?.Invoke(
            GetLocalizedText("merge.status.invalidTrackItems.title", "轨道片段已标记为失效"),
            notificationMessage);
    }

    public async Task ImportPathsAsync(IEnumerable<string> inputPaths)
    {
        ArgumentNullException.ThrowIfNull(inputPaths);

        if (IsVideoJoinProcessing)
        {
            SetProcessingLockedStatusMessage(
                "merge.label.module.merge",
                "合并",
                "merge.label.operation.importMedia",
                "导入新素材");
            return;
        }

        try
        {
            var normalizedInputPaths = inputPaths
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(NormalizeSourcePath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (normalizedInputPaths.Length == 0)
            {
                return;
            }

            var discoveryService = _mediaImportDiscoveryService ?? new MediaImportDiscoveryService();
            var discoveryResult = discoveryService.Discover(normalizedInputPaths, _supportedImportFileTypes);
            var (addedCount, duplicateCount) = await AddSupportedMediaItemsAsync(discoveryResult.SupportedFiles);

            SetDiscoveredImportStatusMessage(
                addedCount,
                duplicateCount,
                discoveryResult.UnsupportedEntries,
                discoveryResult.MissingEntries,
                discoveryResult.UnavailableDirectories);
        }
        catch (OperationCanceledException)
        {
            SetStatusMessage("merge.status.import.cancelled", "已取消素材导入。");
        }
        catch (Exception exception)
        {
            SetStatusMessage("merge.status.import.failed", "导入素材失败，请稍后重试。");
            _logger?.Log(LogLevel.Error, "通过拖拽导入合并素材时发生异常。", exception);
        }
    }

    public void RemoveVideoTrackItem(TrackItem trackItem)
    {
        ArgumentNullException.ThrowIfNull(trackItem);

        if (IsVideoJoinProcessing)
        {
            SetProcessingLockedStatusMessage(
                "merge.label.module.merge",
                "合并",
                "merge.label.operation.adjustTrack",
                "调整轨道");
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
            SetStatusMessage(
                "merge.status.audioVideoCompose.videoTrackRemoved",
                "{fileName} 已从音视频合成的视频轨道移除。",
                ("fileName", removedClipName));
            return;
        }

        var removedPresetClip = ReferenceEquals(GetEffectiveVideoResolutionPresetItem(), trackItem);
        targetCollection.Remove(trackItem);

        var fallbackPresetTrackItem = GetEffectiveVideoResolutionPresetItem();
        if (removedPresetClip && fallbackPresetTrackItem is not null)
        {
            SetStatusMessage(
                "merge.status.videoTrackRemoved.withFallbackPreset",
                "{fileName} 已从视频轨道移除，分辨率预设已切换为 {presetName}。",
                ("fileName", removedClipName),
                ("presetName", fallbackPresetTrackItem.SourceName));
        }
        else
        {
            SetStatusMessage(
                "merge.status.videoTrackRemoved.default",
                "{fileName} 已从视频轨道移除。",
                ("fileName", removedClipName));
        }
    }

    public void SetVideoResolutionPreset(TrackItem trackItem)
    {
        ArgumentNullException.ThrowIfNull(trackItem);

        if (IsVideoJoinProcessing)
        {
            SetProcessingLockedStatusMessage(
                "merge.label.module.videoJoin",
                "视频拼接",
                "merge.label.operation.adjustVideoResolutionPreset",
                "调整分辨率预设");
            return;
        }

        if (!_videoJoinVideoTrackItems.Contains(trackItem))
        {
            return;
        }

        if (!trackItem.CanSetAsResolutionPreset)
        {
            SetStatusMessage(
                "merge.status.videoResolutionPreset.unavailable",
                "当前片段引用的源素材已失效，无法设为分辨率预设。");
            return;
        }

        _manualVideoResolutionPresetTrackId = trackItem.TrackId;
        RefreshTrackCollection(_videoJoinVideoTrackItems, supportsVideoPreset: true);
        RaiseTrackStatePropertiesChanged();
        SetStatusMessage(
            "merge.status.videoResolutionPreset.updated",
            "{name} 已设为分辨率预设。",
            ("name", trackItem.SourceName));
    }

    public void RemoveAudioTrackItem(TrackItem trackItem)
    {
        ArgumentNullException.ThrowIfNull(trackItem);

        if (IsVideoJoinProcessing)
        {
            SetProcessingLockedStatusMessage(
                "merge.label.module.merge",
                "合并",
                "merge.label.operation.adjustTrack",
                "调整轨道");
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
            SetStatusMessage(
                "merge.status.audioVideoCompose.audioTrackRemoved",
                "{fileName} 已从音视频合成的音频轨道移除。",
                ("fileName", removedClipName));
            return;
        }

        var removedPresetClip = _selectedAudioJoinParameterMode == AudioJoinParameterMode.Preset &&
                                 ReferenceEquals(GetEffectiveAudioParameterPresetItem(), trackItem);
        targetCollection.Remove(trackItem);

        if (_selectedAudioJoinParameterMode != AudioJoinParameterMode.Preset)
        {
            SetStatusMessage(
                "merge.status.audioTrackRemoved.default",
                "{fileName} 已从音频轨道移除。",
                ("fileName", removedClipName));
            return;
        }

        var fallbackPresetTrackItem = GetEffectiveAudioParameterPresetItem();
        if (removedPresetClip && fallbackPresetTrackItem is not null)
        {
            SetStatusMessage(
                "merge.status.audioTrackRemoved.withFallbackPreset",
                "{fileName} 已从音频轨道移除，参数预设已切换为 {presetName}。",
                ("fileName", removedClipName),
                ("presetName", fallbackPresetTrackItem.SourceName));
        }
        else
        {
            SetStatusMessage(
                "merge.status.audioTrackRemoved.default",
                "{fileName} 已从音频轨道移除。",
                ("fileName", removedClipName));
        }
    }

    public void SetAudioParameterPreset(TrackItem trackItem)
    {
        ArgumentNullException.ThrowIfNull(trackItem);

        if (IsVideoJoinProcessing)
        {
            SetProcessingLockedStatusMessage(
                "merge.label.module.audioJoin",
                "音频拼接",
                "merge.label.operation.adjustAudioParameterPreset",
                "调整参数预设");
            return;
        }

        if (_selectedAudioJoinParameterMode != AudioJoinParameterMode.Preset)
        {
            SetStatusMessage(
                "merge.status.audioParameterPreset.balancedModeOnly",
                "当前为均衡模式，不支持手动参数预设。");
            return;
        }

        if (!_audioJoinAudioTrackItems.Contains(trackItem))
        {
            return;
        }

        if (!trackItem.CanSetAsResolutionPreset)
        {
            SetStatusMessage(
                "merge.status.audioParameterPreset.unavailable",
                "当前片段引用的源素材已失效，无法设为参数预设。");
            return;
        }

        _manualAudioParameterPresetTrackId = trackItem.TrackId;
        RefreshTrackCollection(_audioJoinAudioTrackItems, supportsVideoPreset: false);
        RaiseTrackStatePropertiesChanged();
        SetStatusMessage(
            "merge.status.audioParameterPreset.updated",
            "{name} 已设为音频参数预设。",
            ("name", trackItem.SourceName));
    }

    private async Task ImportFilesAsync()
    {
        if (_filePickerService is null)
        {
            SetStatusMessage("merge.status.import.filePicker.unsupported", "当前环境暂不支持打开文件选择器。");
            return;
        }

        try
        {
            var selectedFiles = await _filePickerService.PickFilesAsync(
                new FilePickerRequest(
                    _supportedImportFileTypes,
                    GetLocalizedText("merge.dialog.importFiles.title", "导入文件")));

            if (selectedFiles.Count == 0)
            {
                SetStatusMessage("merge.status.import.filePicker.cancelled", "已取消文件导入。");
                return;
            }

            var (addedCount, duplicateCount) = await AddSupportedMediaItemsAsync(selectedFiles);
            SetFilePickerImportStatusMessage(addedCount, duplicateCount);
        }
        catch (OperationCanceledException)
        {
            SetStatusMessage("merge.status.import.filePicker.cancelled", "已取消文件导入。");
        }
        catch (Exception exception)
        {
            SetStatusMessage("merge.status.import.filePicker.failed", "导入文件失败，请稍后重试。");
            _logger?.Log(LogLevel.Error, "导入合并素材时发生异常。", exception);
        }
    }

    private async Task<(int AddedCount, int DuplicateCount)> AddSupportedMediaItemsAsync(IEnumerable<string> filePaths)
    {
        ArgumentNullException.ThrowIfNull(filePaths);

        var existingPaths = new HashSet<string>(
            _mediaItems
                .Select(item => item.SourcePath)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(NormalizeSourcePath),
            StringComparer.OrdinalIgnoreCase);

        var addedCount = 0;
        var duplicateCount = 0;

        foreach (var filePath in filePaths)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                continue;
            }

            var normalizedPath = NormalizeSourcePath(filePath);
            if (!existingPaths.Add(normalizedPath))
            {
                duplicateCount++;
                continue;
            }

            _mediaItems.Add(await CreateMediaItemAsync(normalizedPath));
            addedCount++;
        }

        return (addedCount, duplicateCount);
    }

    private void SetDiscoveredImportStatusMessage(
        int addedCount,
        int duplicateCount,
        int unsupportedEntries,
        int missingEntries,
        int unavailableDirectories)
    {
        var hasDetails = duplicateCount > 0 ||
                         unsupportedEntries > 0 ||
                         missingEntries > 0 ||
                         unavailableDirectories > 0;

        if (addedCount > 0)
        {
            if (hasDetails)
            {
                SetStatusMessage(
                    "merge.status.import.completed.withDetails",
                    "已导入 {count} 个素材，{details}。",
                    ("count", addedCount),
                    LocalizedArgument(
                        "details",
                        () => BuildImportDetailsText(
                            duplicateCount,
                            unsupportedEntries,
                            missingEntries,
                            unavailableDirectories)));
                return;
            }

            SetStatusMessage(
                "merge.status.import.completed.simple",
                "已导入 {count} 个素材。",
                ("count", addedCount));
            return;
        }

        if (hasDetails)
        {
            SetStatusMessage(
                "merge.status.import.skipped.withDetails",
                "未导入新的素材，{details}。",
                LocalizedArgument(
                    "details",
                    () => BuildImportDetailsText(
                        duplicateCount,
                        unsupportedEntries,
                        missingEntries,
                        unavailableDirectories)));
            return;
        }

        SetStatusMessage("merge.status.import.skipped.simple", "未导入新的素材。");
    }

    private void SetFilePickerImportStatusMessage(int addedCount, int duplicateCount)
    {
        if (addedCount > 0)
        {
            if (duplicateCount > 0)
            {
                SetStatusMessage(
                    "merge.status.import.filePicker.completed.withDuplicates",
                    "已导入 {count} 个素材，{duplicateCount} 个重复文件已跳过。",
                    ("count", addedCount),
                    ("duplicateCount", duplicateCount));
                return;
            }

            SetStatusMessage(
                "merge.status.import.filePicker.completed.ordered",
                "已导入 {count} 个素材，列表按导入顺序排列。",
                ("count", addedCount));
            return;
        }

        if (duplicateCount > 0)
        {
            SetStatusMessage(
                "merge.status.import.filePicker.skipped.duplicateOnly",
                "所选文件已存在于素材列表中。");
            return;
        }

        SetStatusMessage("merge.status.import.skipped.simple", "未导入新的素材。");
    }

    private string BuildImportDetailsText(
        int duplicateCount,
        int unsupportedEntries,
        int missingEntries,
        int unavailableDirectories)
    {
        var details = new List<string>();

        if (duplicateCount > 0)
        {
            details.Add(FormatLocalizedText(
                "merge.status.import.details.duplicate",
                "{count} 个重复素材已跳过",
                ("count", duplicateCount)));
        }

        if (unsupportedEntries > 0)
        {
            details.Add(FormatLocalizedText(
                "merge.status.import.details.unsupported",
                "{count} 个不支持的文件已排除",
                ("count", unsupportedEntries)));
        }

        if (missingEntries > 0)
        {
            details.Add(FormatLocalizedText(
                "merge.status.import.details.missing",
                "{count} 个路径不存在",
                ("count", missingEntries)));
        }

        if (unavailableDirectories > 0)
        {
            details.Add(FormatLocalizedText(
                "merge.status.import.details.unavailableDirectory",
                "{count} 个文件夹无法访问",
                ("count", unavailableDirectories)));
        }

        return string.Join(GetLocalizedText("merge.common.listSeparator", "，"), details);
    }

    private async Task BrowseOutputDirectoryAsync()
    {
        if (_filePickerService is null)
        {
            SetStatusMessage("merge.status.outputDirectory.picker.unsupported", "当前环境暂不支持打开目录选择器。");
            return;
        }

        try
        {
            var selectedFolder = await _filePickerService.PickFolderAsync(
                GetLocalizedText("merge.dialog.outputDirectory.title", "选择输出目录"));
            if (string.IsNullOrWhiteSpace(selectedFolder))
            {
                SetStatusMessage("merge.status.outputDirectory.selection.cancelled", "已取消选择输出目录。");
                return;
            }

            switch (_selectedMergeMode)
            {
                case MergeWorkspaceMode.AudioJoin:
                    AudioJoinOutputDirectory = selectedFolder;
                    SetStatusMessage(
                        "merge.status.outputDirectory.audioJoin.updated",
                        "已将音频拼接输出目录设置为：{path}",
                        ("path", AudioJoinOutputDirectory));
                    break;
                case MergeWorkspaceMode.AudioVideoCompose:
                    AudioVideoComposeOutputDirectory = selectedFolder;
                    SetStatusMessage(
                        "merge.status.outputDirectory.audioVideoCompose.updated",
                        "已将音视频合成输出目录设置为：{path}",
                        ("path", AudioVideoComposeOutputDirectory));
                    break;
                default:
                    OutputDirectory = selectedFolder;
                    SetStatusMessage(
                        "merge.status.outputDirectory.videoJoin.updated",
                        "已将视频拼接输出目录设置为：{path}",
                        ("path", OutputDirectory));
                    break;
            }
        }
        catch (OperationCanceledException)
        {
            SetStatusMessage("merge.status.outputDirectory.selection.cancelled", "已取消选择输出目录。");
        }
        catch (Exception exception)
        {
            SetStatusMessage("merge.status.outputDirectory.selection.failed", "选择输出目录失败，请稍后重试。");
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
            SetStatusMessage(
                "merge.status.outputDirectory.audioJoin.cleared",
                "已清空音频拼接输出目录，处理时将恢复为当前音频基准素材所在文件夹。");
            return;
        }

        if (_selectedMergeMode == MergeWorkspaceMode.AudioVideoCompose)
        {
            AudioVideoComposeOutputDirectory = string.Empty;
            SetStatusMessage(
                "merge.status.outputDirectory.audioVideoCompose.cleared",
                "已清空音视频合成输出目录，处理时将恢复为当前视频素材所在文件夹。");
            return;
        }

        OutputDirectory = string.Empty;
        SetStatusMessage(
            "merge.status.outputDirectory.videoJoin.cleared",
            "已清空视频拼接输出目录，处理时将恢复为分辨率预设视频所在文件夹输出。");
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
            SetStatusMessage(CreateVideoJoinCannotStartStatusState());
            return;
        }

        if (_videoJoinWorkflowService is null || _mergeMediaAnalysisService is null)
        {
            SetStatusMessage("merge.status.videoJoin.cannotStart.unsupported", "当前环境暂不支持视频拼接输出。");
            return;
        }

        _videoJoinProcessingCancellationSource?.Dispose();
        _videoJoinProcessingCancellationSource = new CancellationTokenSource();
        var cancellationToken = _videoJoinProcessingCancellationSource.Token;

        try
        {
            IsVideoJoinProcessing = true;
            ShowProcessingPreparationProgress(
                "merge.progress.videoJoin.summary",
                "视频拼接",
                "merge.progress.videoJoin.preparing",
                "正在检查视频拼接素材并准备输出参数...");
            SetStatusMessage("merge.progress.videoJoin.preparing", "正在检查视频拼接素材并准备输出参数...");

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

            SetStatusMessage("merge.status.runtime.preparing", "正在准备 FFmpeg 运行时...");
            await _videoJoinWorkflowService.EnsureRuntimeReadyAsync(cancellationToken);

            SetStatusMessage(
                "merge.status.videoJoin.runtimeReady",
                "FFmpeg 已就绪，正在拼接 {count} 段视频...",
                ("count", segments.Count));
            var progressReporter = new Progress<FFmpegProgressUpdate>(update => HandleVideoJoinProgress(update, segments.Count));
            var exportResult = await _videoJoinWorkflowService.ExportAsync(
                request,
                progressReporter,
                () => SetStatusMessage("merge.status.transcoding.gpuFallback", "GPU 编码失败，已自动回退为 CPU 重试一次。"),
                cancellationToken);
            var result = exportResult.ExecutionResult;

            if (result.WasSuccessful && File.Exists(exportResult.Request.OutputPath))
            {
                SetVideoJoinCompletionStatusMessage(exportResult.Request, presetTrackItem, exportResult.TranscodingMessage);
                TryRevealVideoJoinOutputFile(exportResult.Request.OutputPath);
                return;
            }

            if (result.WasCancelled)
            {
                SetStatusMessage("merge.status.videoJoin.cancelled", "已取消视频拼接任务。");
            }
            else
            {
                SetVideoJoinFailureStatusMessage(result, exportResult.TranscodingMessage);
            }
        }
        catch (OperationCanceledException) when (_videoJoinProcessingCancellationSource?.IsCancellationRequested == true)
        {
            SetStatusMessage("merge.status.videoJoin.cancelled", "已取消视频拼接任务。");
        }
        catch (Exception exception)
        {
            SetStatusMessage(
                "merge.status.videoJoin.exception",
                "视频拼接任务执行失败：{message}",
                ("message", exception.Message));
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
            SetStatusMessage(CreateAudioJoinCannotStartStatusState());
            return;
        }

        if (_audioJoinWorkflowService is null || _mergeMediaAnalysisService is null)
        {
            SetStatusMessage("merge.status.audioJoin.cannotStart.unsupported", "当前环境暂不支持音频拼接输出。");
            return;
        }

        _videoJoinProcessingCancellationSource?.Dispose();
        _videoJoinProcessingCancellationSource = new CancellationTokenSource();
        var cancellationToken = _videoJoinProcessingCancellationSource.Token;

        try
        {
            IsVideoJoinProcessing = true;
            ShowProcessingPreparationProgress(
                "merge.progress.audioJoin.summary",
                "音频拼接",
                "merge.progress.audioJoin.preparing",
                "正在检查音频拼接素材并准备输出参数...");
            SetStatusMessage("merge.progress.audioJoin.preparing", "正在检查音频拼接素材并准备输出参数...");

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

            SetStatusMessage("merge.status.runtime.preparing", "正在准备 FFmpeg 运行时...");
            await _audioJoinWorkflowService.EnsureRuntimeReadyAsync(cancellationToken);

            SetStatusMessage(
                "merge.status.audioJoin.runtimeReady",
                "FFmpeg 已就绪，正在拼接 {count} 段音频...",
                ("count", segments.Count));
            var progressReporter = new Progress<FFmpegProgressUpdate>(update => HandleAudioJoinProgress(update, segments.Count));
            var exportResult = await _audioJoinWorkflowService.ExportAsync(request, progressReporter, cancellationToken);
            var result = exportResult.ExecutionResult;

            if (result.WasSuccessful && File.Exists(exportResult.Request.OutputPath))
            {
                SetAudioJoinCompletionStatusMessage(exportResult.Request, presetTrackItem, exportResult.TranscodingMessage);
                TryRevealVideoJoinOutputFile(exportResult.Request.OutputPath);
                return;
            }

            if (result.WasCancelled)
            {
                SetStatusMessage("merge.status.audioJoin.cancelled", "已取消音频拼接任务。");
            }
            else
            {
                SetAudioJoinFailureStatusMessage(result, exportResult.TranscodingMessage);
            }
        }
        catch (OperationCanceledException) when (_videoJoinProcessingCancellationSource?.IsCancellationRequested == true)
        {
            SetStatusMessage("merge.status.audioJoin.cancelled", "已取消音频拼接任务。");
        }
        catch (Exception exception)
        {
            SetStatusMessage(
                "merge.status.audioJoin.exception",
                "音频拼接任务执行失败：{message}",
                ("message", exception.Message));
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

    private LocalizedTextState CreateVideoJoinCannotStartStatusState()
    {
        if (_selectedMergeMode != MergeWorkspaceMode.VideoJoin)
        {
            return new LocalizedTextState(
                "merge.status.videoJoin.cannotStart.invalidMode",
                "当前开始处理仅适用于视频拼接模式。");
        }

        if (IsVideoJoinProcessing)
        {
            return new LocalizedTextState(
                "merge.status.videoJoin.cannotStart.processing",
                "视频拼接任务正在进行中。");
        }

        if (_videoJoinWorkflowService is null)
        {
            return new LocalizedTextState(
                "merge.status.videoJoin.cannotStart.unsupported",
                "当前环境暂不支持视频拼接输出。");
        }

        return new LocalizedTextState(
            "merge.status.videoJoin.cannotStart.missingTrack",
            "请先向视频轨道添加至少一个有效视频片段。");
    }

    private LocalizedTextState CreateAudioJoinCannotStartStatusState()
    {
        if (_selectedMergeMode != MergeWorkspaceMode.AudioJoin)
        {
            return new LocalizedTextState(
                "merge.status.audioJoin.cannotStart.invalidMode",
                "当前开始处理仅适用于音频拼接模式。");
        }

        if (IsVideoJoinProcessing)
        {
            return new LocalizedTextState(
                "merge.status.audioJoin.cannotStart.processing",
                "音频拼接任务正在进行中。");
        }

        if (_audioJoinWorkflowService is null)
        {
            return new LocalizedTextState(
                "merge.status.audioJoin.cannotStart.unsupported",
                "当前环境暂不支持音频拼接输出。");
        }

        return new LocalizedTextState(
            "merge.status.audioJoin.cannotStart.missingTrack",
            "请先向音频轨道添加至少一个有效音频片段。");
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

    private void SetVideoJoinCompletionStatusMessage(
        VideoJoinExportRequest request,
        TrackItem presetTrackItem,
        string? transcodingMessage)
    {
        var fileName = Path.GetFileName(request.OutputPath);
        if (string.IsNullOrWhiteSpace(transcodingMessage))
        {
            SetStatusMessage(
                "merge.status.videoJoin.completed",
                "视频拼接完成：{fileName}。预设分辨率来源：{presetName} · {resolution}；较小分辨率视频：{smallerStrategy}；较大分辨率视频：{largerStrategy}。",
                ("fileName", fileName),
                ("presetName", presetTrackItem.SourceName),
                ("resolution", presetTrackItem.ResolutionText),
                LocalizedArgument("smallerStrategy", () => GetSmallerResolutionStrategyLabel()),
                LocalizedArgument("largerStrategy", () => GetLargerResolutionStrategyLabel()));
            return;
        }

        SetStatusMessage(
            "merge.status.videoJoin.completed.withTranscoding",
            "视频拼接完成：{fileName}。预设分辨率来源：{presetName} · {resolution}；较小分辨率视频：{smallerStrategy}；较大分辨率视频：{largerStrategy}。{transcoding}",
            ("fileName", fileName),
            ("presetName", presetTrackItem.SourceName),
            ("resolution", presetTrackItem.ResolutionText),
            LocalizedArgument("smallerStrategy", () => GetSmallerResolutionStrategyLabel()),
            LocalizedArgument("largerStrategy", () => GetLargerResolutionStrategyLabel()),
            LocalizedArgument("transcoding", () => ResolveMergeTranscodingMessage(transcodingMessage)));
    }

    private void SetVideoJoinFailureStatusMessage(FFmpegExecutionResult result, string? transcodingMessage)
    {
        if (string.IsNullOrWhiteSpace(transcodingMessage))
        {
            SetStatusMessage(
                "merge.status.videoJoin.failed",
                "视频拼接失败：{reason}",
                LocalizedArgument("reason", () => ExtractFriendlyVideoJoinFailureMessage(result)));
            return;
        }

        SetStatusMessage(
            "merge.status.videoJoin.failed.withTranscoding",
            "视频拼接失败：{reason} {transcoding}",
            LocalizedArgument("reason", () => ExtractFriendlyVideoJoinFailureMessage(result)),
            LocalizedArgument("transcoding", () => ResolveMergeTranscodingMessage(transcodingMessage)));
    }

    private void SetAudioJoinCompletionStatusMessage(
        AudioJoinExportRequest request,
        TrackItem? presetTrackItem,
        string? transcodingMessage)
    {
        var fileName = Path.GetFileName(request.OutputPath);
        var summaryArgument = LocalizedArgument(
            "summary",
            () => BuildAudioJoinOutputSummaryText(
                request.TargetSampleRate,
                request.TargetBitrate,
                request.OutputFormat,
                request.ParameterMode));
        var transcodingArgument = LocalizedArgument(
            "transcoding",
            () => ResolveMergeTranscodingMessage(transcodingMessage));

        if (request.ParameterMode == AudioJoinParameterMode.Preset && presetTrackItem is not null)
        {
            SetStatusMessage(
                string.IsNullOrWhiteSpace(transcodingMessage)
                    ? "merge.status.audioJoin.completed.withPreset"
                    : "merge.status.audioJoin.completed.withPresetTranscoding",
                string.IsNullOrWhiteSpace(transcodingMessage)
                    ? "音频拼接完成：{fileName}。参数预设来源：{presetName} · {summary}。"
                    : "音频拼接完成：{fileName}。参数预设来源：{presetName} · {summary}。{transcoding}",
                ("fileName", fileName),
                ("presetName", presetTrackItem.SourceName),
                summaryArgument,
                transcodingArgument);
            return;
        }

        SetStatusMessage(
            string.IsNullOrWhiteSpace(transcodingMessage)
                ? "merge.status.audioJoin.completed.balanced"
                : "merge.status.audioJoin.completed.balancedTranscoding",
            string.IsNullOrWhiteSpace(transcodingMessage)
                ? "音频拼接完成：{fileName}。当前模式：均衡模式 · {summary}。"
                : "音频拼接完成：{fileName}。当前模式：均衡模式 · {summary}。{transcoding}",
            ("fileName", fileName),
            summaryArgument,
            transcodingArgument);
    }

    private void SetAudioJoinFailureStatusMessage(FFmpegExecutionResult result, string? transcodingMessage)
    {
        if (string.IsNullOrWhiteSpace(transcodingMessage))
        {
            SetStatusMessage(
                "merge.status.audioJoin.failed",
                "音频拼接失败：{reason}",
                LocalizedArgument("reason", () => ExtractFriendlyVideoJoinFailureMessage(result)));
            return;
        }

        SetStatusMessage(
            "merge.status.audioJoin.failed.withTranscoding",
            "音频拼接失败：{reason} {transcoding}",
            LocalizedArgument("reason", () => ExtractFriendlyVideoJoinFailureMessage(result)),
            LocalizedArgument("transcoding", () => ResolveMergeTranscodingMessage(transcodingMessage)));
    }

    private UserPreferences GetCurrentUserPreferences() =>
        _userPreferencesService?.Load() ?? new UserPreferences();

    private string FormatAudioJoinSampleRateText(int sampleRate)
    {
        if (sampleRate <= 0)
        {
            return GetLocalizedText("merge.summary.audioJoinOutput.sampleRate.unknown", "未知采样率");
        }

        return sampleRate >= 1_000
            ? $"{sampleRate / 1_000d:0.###} kHz"
            : $"{sampleRate:0} Hz";
    }

    private string FormatAudioJoinBitrateText(int bitrate)
    {
        if (bitrate <= 0)
        {
            return GetLocalizedText("merge.summary.audioJoinOutput.bitrate.automatic", "自动码率");
        }

        return bitrate >= 1_000_000
            ? $"{bitrate / 1_000_000d:0.##} Mbps"
            : $"{bitrate / 1_000d:0.##} kbps";
    }

    private string BuildAudioJoinOutputSummaryText(
        int sampleRate,
        int? requestedBitrate,
        OutputFormatOption outputFormat,
        AudioJoinParameterMode parameterMode)
    {
        var sampleRateText = FormatAudioJoinSampleRateText(sampleRate);
        var bitrateText = ResolveAudioJoinOutputBitrateText(sampleRate, requestedBitrate, outputFormat, parameterMode);
        return FormatLocalizedText(
            "merge.summary.audioJoinOutput.value",
            "{sampleRate} · {bitrate}",
            ("sampleRate", sampleRateText),
            ("bitrate", bitrateText));
    }

    private string ResolveAudioJoinOutputBitrateText(
        int sampleRate,
        int? requestedBitrate,
        OutputFormatOption outputFormat,
        AudioJoinParameterMode parameterMode)
    {
        var effectiveBitrate = ResolveAudioJoinEffectiveBitrate(sampleRate, requestedBitrate, outputFormat, parameterMode);
        return effectiveBitrate is > 0
            ? FormatAudioJoinBitrateText(effectiveBitrate.Value)
            : FormatLocalizedText(
                "merge.summary.audioJoinOutput.bitrate.auto",
                "{format} 编码器自动码率",
                ("format", outputFormat.DisplayName));
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

    private string ExtractFriendlyVideoJoinFailureMessage(FFmpegExecutionResult result)
    {
        if (result.WasCancelled)
        {
            return GetLocalizedText("merge.status.failure.cancelled", "当前任务已取消。");
        }

        if (result.TimedOut)
        {
            return GetLocalizedText("merge.status.failure.timedOut", "当前任务已超时。");
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
                ? FormatLocalizedText(
                    "merge.status.failure.exitCode",
                    "FFmpeg 已退出，返回代码：{code}。",
                    ("code", exitCode))
                : GetLocalizedText("merge.status.failure.noReadableMessage", "FFmpeg 未返回可读错误信息。");
    }

    private string ResolveMergeTranscodingMessage(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return string.Empty;
        }

        return message switch
        {
            "当前素材参数不完全一致，或当前流程需要统一采样率 / 声道布局，本次已回退为兼容音频转码。"
                => GetLocalizedText(
                    "merge.status.transcoding.audioJoin.compatibilityFallback",
                    "当前素材参数不完全一致，或当前流程需要统一采样率 / 声道布局，本次已回退为兼容音频转码。"),
            "当前素材无法完整复用原始音频流，本次已自动回退为兼容音频转码。"
                => GetLocalizedText(
                    "merge.status.transcoding.audioJoin.autoFallback",
                    "当前素材无法完整复用原始音频流，本次已自动回退为兼容音频转码。"),
            "当前流程无法完整复用原始视频流，本次将回退为兼容转码。"
                => GetLocalizedText(
                    "merge.status.transcoding.audioVideoCompose.compatibilityFallback",
                    "当前流程无法完整复用原始视频流，本次将回退为兼容转码。"),
            "当前流程无法完整复用原始视频流，本次已自动回退为兼容转码。"
                => GetLocalizedText(
                    "merge.status.transcoding.audioVideoCompose.autoFallback",
                    "当前流程无法完整复用原始视频流，本次已自动回退为兼容转码。"),
            "当前素材参数不完全一致，或当前流程需要统一分辨率 / 帧率 / 音频参数，本次已回退为兼容转码。"
                => GetLocalizedText(
                    "merge.status.transcoding.videoJoin.compatibilityFallback",
                    "当前素材参数不完全一致，或当前流程需要统一分辨率 / 帧率 / 音频参数，本次已回退为兼容转码。"),
            "当前素材无法完整复用原始流，本次已自动回退为兼容转码。"
                => GetLocalizedText(
                    "merge.status.transcoding.videoJoin.autoFallback",
                    "当前素材无法完整复用原始流，本次已自动回退为兼容转码。"),
            "GPU 编码失败，已自动回退为 CPU 重试一次。"
                => GetLocalizedText(
                    "merge.status.transcoding.gpuFallback",
                    "GPU 编码失败，已自动回退为 CPU 重试一次。"),
            _ => message
        };
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
        SetStatusMessage(
            strategy == MergeSmallerResolutionStrategy.PadWithBlackBars
                ? "merge.status.videoResolutionStrategy.smaller.pad"
                : "merge.status.videoResolutionStrategy.smaller.stretch",
            strategy == MergeSmallerResolutionStrategy.PadWithBlackBars
                ? "较小分辨率视频将默认填充黑边。"
                : "较小分辨率视频将默认拉伸填充。");
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
        SetStatusMessage(
            strategy == MergeLargerResolutionStrategy.SqueezeToFit
                ? "merge.status.videoResolutionStrategy.larger.squeeze"
                : "merge.status.videoResolutionStrategy.larger.crop",
            strategy == MergeLargerResolutionStrategy.SqueezeToFit
                ? "较大分辨率视频将默认挤压到预设分辨率。"
                : "较大分辨率视频将默认裁剪到预设分辨率。");
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
            ? GetLocalizedText("merge.summary.videoResolutionStrategy.smaller.pad", "填充黑边")
            : GetLocalizedText("merge.summary.videoResolutionStrategy.smaller.stretch", "拉伸填充");

    private string GetLargerResolutionStrategyLabel() =>
        _selectedLargerResolutionStrategy == MergeLargerResolutionStrategy.SqueezeToFit
            ? GetLocalizedText("merge.summary.videoResolutionStrategy.larger.squeeze", "挤压")
            : GetLocalizedText("merge.summary.videoResolutionStrategy.larger.crop", "裁剪");

    private void SetAudioJoinParameterMode(AudioJoinParameterMode parameterMode)
    {
        if (_selectedAudioJoinParameterMode == parameterMode)
        {
            return;
        }

        if (IsVideoJoinProcessing)
        {
            SetProcessingLockedStatusMessage(
                "merge.label.module.audioJoin",
                "音频拼接",
                "merge.label.operation.switchAudioParameterMode",
                "切换参数模式");
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
        SetStatusMessage(
            parameterMode == AudioJoinParameterMode.Preset
                ? "merge.status.audioParameterMode.preset"
                : "merge.status.audioParameterMode.balanced",
            parameterMode == AudioJoinParameterMode.Preset
                ? "已切换到指定预设模式，可在音频轨道上选择一段音频作为参数预设。"
                : "已切换到均衡模式，系统将按全部有效音轨自动统一参数。");
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

    private void SetMergeMode(MergeWorkspaceMode mergeMode)
    {
        if (_selectedMergeMode == mergeMode)
        {
            return;
        }

        if (IsVideoJoinProcessing)
        {
            SetProcessingLockedStatusMessage(
                "merge.label.module.merge",
                "合并",
                "merge.label.operation.switchMode",
                "切换模式");
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
        ClearModeMismatchWarning();
        NotifyCommandStates();

        SetStatusMessage(
            $"{GetModeState(mergeMode).Profile.LocalizationKeyPrefix}.selectionMessage",
            GetModeState(mergeMode).Profile.SelectionMessage);

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

    private string BuildInvalidTrackItemsMessage(int invalidatedCount) =>
        FormatLocalizedText(
            "merge.status.invalidTrackItems.message",
            "检测到 {count} 个轨道片段引用的源素材已从素材列表中移除。相关片段已标记为失效，将不会参与当前合并输出。请重新添加源素材，或直接从轨道中移除这些失效片段。",
            ("count", invalidatedCount));
}
