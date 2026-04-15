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

public sealed class MergeViewModel : ObservableObject
{
    private readonly ObservableCollection<MediaItem> _mediaItems;
    private readonly ObservableCollection<TrackItem> _videoJoinVideoTrackItems;
    private readonly ObservableCollection<TrackItem> _audioJoinAudioTrackItems;
    private readonly ObservableCollection<TrackItem> _audioVideoComposeVideoTrackItems;
    private readonly ObservableCollection<TrackItem> _audioVideoComposeAudioTrackItems;
    private readonly ObservableCollection<TrackItem> _emptyTrackItems;
    private readonly IReadOnlyList<OutputFormatOption> _videoJoinOutputFormats;
    private readonly IReadOnlyList<OutputFormatOption> _audioJoinOutputFormats;
    private readonly AsyncRelayCommand _importFilesCommand;
    private readonly AsyncRelayCommand _browseOutputDirectoryCommand;
    private readonly RelayCommand _clearOutputDirectoryCommand;
    private readonly AsyncRelayCommand _startVideoJoinProcessingCommand;
    private readonly RelayCommand _cancelVideoJoinProcessingCommand;
    private readonly IFilePickerService? _filePickerService;
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

    public MergeViewModel(
        IFilePickerService? filePickerService = null,
        IMediaInfoService? mediaInfoService = null,
        IUserPreferencesService? userPreferencesService = null,
        IVideoJoinWorkflowService? videoJoinWorkflowService = null,
        IAudioJoinWorkflowService? audioJoinWorkflowService = null,
        IFileRevealService? fileRevealService = null,
        ApplicationConfiguration? configuration = null,
        ILogger? logger = null)
    {
        var effectiveConfiguration = configuration ?? new ApplicationConfiguration();
        var preferences = userPreferencesService?.Load() ?? new UserPreferences();

        _filePickerService = filePickerService;
        _videoJoinWorkflowService = videoJoinWorkflowService;
        _audioJoinWorkflowService = audioJoinWorkflowService;
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

    private ObservableCollection<TrackItem> ActiveVideoTrackItems => GetVideoTrackItems(_selectedMergeMode);

    private ObservableCollection<TrackItem> ActiveAudioTrackItems => GetAudioTrackItems(_selectedMergeMode);

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

    public string OutputDirectoryHintText => "默认使用当前预设素材所在文件夹；设置后，处理结果会统一输出到所选文件夹。";

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

    public string TimelineHintText => _selectedMergeMode switch
    {
        MergeWorkspaceMode.VideoJoin => "当前为视频拼接模式，仅可将视频素材添加到视频轨道。",
        MergeWorkspaceMode.AudioJoin => "当前为音频拼接模式，仅可将音频素材添加到音频轨道。",
        _ => "当前为音视频合成模式，素材会自动进入对应轨道。"
    };

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
        _selectedMergeMode == MergeWorkspaceMode.VideoJoin ? Visibility.Visible : Visibility.Collapsed;

    public Visibility AudioJoinTimelineVisibility =>
        _selectedMergeMode == MergeWorkspaceMode.AudioJoin ? Visibility.Visible : Visibility.Collapsed;

    public Visibility StandardTimelineVisibility =>
        _selectedMergeMode == MergeWorkspaceMode.AudioVideoCompose ? Visibility.Visible : Visibility.Collapsed;

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
            if (_audioJoinAudioTrackItems.Count == 0)
            {
                return "添加音频片段后，将默认以首个可用音频作为参数预设。";
            }

            var presetTrackItem = GetEffectiveAudioParameterPresetItem();
            if (presetTrackItem is null)
            {
                return "当前暂无可用的音频片段可作为参数预设。";
            }

            return HasManualAudioParameterPresetSelection()
                ? $"当前音频参数预设：{presetTrackItem.SourceName} · {presetTrackItem.ResolutionText}"
                : $"当前默认以首个可用音频作为参数预设：{presetTrackItem.SourceName} · {presetTrackItem.ResolutionText}";
        }
    }

    public string VideoTrackEmptyText => _selectedMergeMode switch
    {
        MergeWorkspaceMode.AudioJoin => "当前模式聚焦音频拼接，视频轨道暂不参与编排。",
        _ => "从素材列表单击视频文件，可将其添加到视频轨道。"
    };

    public string AudioTrackEmptyText => _selectedMergeMode switch
    {
        MergeWorkspaceMode.VideoJoin => "当前模式聚焦视频拼接，音频轨道暂不参与编排。",
        _ => "从素材列表单击音频文件，可将其添加到音频轨道。"
    };

    public void AddMediaToTimeline(MediaItem mediaItem)
    {
        ArgumentNullException.ThrowIfNull(mediaItem);

        if (IsVideoJoinProcessing)
        {
            StatusMessage = "视频拼接任务处理中，若需调整轨道，请先取消当前任务。";
            return;
        }

        if (!TryResolveTrackCollectionForAddition(mediaItem, out var trackItems, out var rejectionMessage))
        {
            SetModeMismatchWarningVisibility(true, rejectionMessage);
            StatusMessage = rejectionMessage;
            return;
        }

        SetModeMismatchWarningVisibility(false, string.Empty);
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
            StatusMessage = "视频拼接任务处理中，若需调整素材，请先取消当前任务。";
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
            StatusMessage = "视频拼接任务处理中，若需调整轨道，请先取消当前任务。";
            return;
        }

        if (!_videoJoinVideoTrackItems.Contains(trackItem))
        {
            return;
        }

        var removedClipName = trackItem.SourceName;
        var removedPresetClip = ReferenceEquals(GetEffectiveVideoResolutionPresetItem(), trackItem);
        _videoJoinVideoTrackItems.Remove(trackItem);

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
            StatusMessage = "音频拼接任务处理中，若需调整轨道，请先取消当前任务。";
            return;
        }

        if (!_audioJoinAudioTrackItems.Contains(trackItem))
        {
            return;
        }

        var removedClipName = trackItem.SourceName;
        var removedPresetClip = ReferenceEquals(GetEffectiveAudioParameterPresetItem(), trackItem);
        _audioJoinAudioTrackItems.Remove(trackItem);

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

            if (_selectedMergeMode == MergeWorkspaceMode.AudioJoin)
            {
                AudioJoinOutputDirectory = selectedFolder;
            }
            else
            {
                OutputDirectory = selectedFolder;
            }
            StatusMessage = _selectedMergeMode == MergeWorkspaceMode.AudioJoin
                ? $"已将音频拼接输出目录设置为：{AudioJoinOutputDirectory}"
                : $"已将视频拼接输出目录设置为：{OutputDirectory}";
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
            StatusMessage = "已清空音频拼接输出目录，处理时将恢复为当前音频预设素材所在文件夹。";
            return;
        }

        OutputDirectory = string.Empty;
        StatusMessage = "已清空视频拼接输出目录，处理时将恢复为分辨率预设视频所在文件夹输出。";
    }

    private Task StartProcessingAsync() => _selectedMergeMode switch
    {
        MergeWorkspaceMode.VideoJoin => StartVideoJoinProcessingAsync(),
        MergeWorkspaceMode.AudioJoin => StartAudioJoinProcessingAsync(),
        _ => HandleUnsupportedMergeModeAsync()
    };

    private bool CanStartProcessing() => _selectedMergeMode switch
    {
        MergeWorkspaceMode.VideoJoin => CanStartVideoJoinProcessing(),
        MergeWorkspaceMode.AudioJoin => CanStartAudioJoinProcessing(),
        _ => false
    };

    private void CancelProcessing() => CancelVideoJoinProcessing();

    private bool CanEditActiveOutputSettings() =>
        !IsVideoJoinProcessing &&
        (_selectedMergeMode == MergeWorkspaceMode.VideoJoin || _selectedMergeMode == MergeWorkspaceMode.AudioJoin);

    private Task HandleUnsupportedMergeModeAsync()
    {
        StatusMessage = "当前音视频合成模式暂未接入独立导出流程。";
        return Task.CompletedTask;
    }

    private async Task StartVideoJoinProcessingAsync()
    {
        if (!CanStartVideoJoinProcessing())
        {
            StatusMessage = GetVideoJoinCannotStartMessage();
            return;
        }

        if (_videoJoinWorkflowService is null)
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

            var segments = await BuildVideoJoinSegmentsAsync(activeTrackItems, cancellationToken);
            var presetSegment = segments[presetTrackIndex];
            var plannedOutputPath = CreatePlannedVideoJoinOutputPath(presetTrackItem.SourcePath);
            var request = new VideoJoinExportRequest(
                segments,
                plannedOutputPath,
                SelectedOutputFormat,
                presetSegment.Width,
                presetSegment.Height,
                presetSegment.FrameRate,
                _selectedSmallerResolutionStrategy,
                _selectedLargerResolutionStrategy);

            StatusMessage = "正在准备 FFmpeg 运行时...";
            await _videoJoinWorkflowService.EnsureRuntimeReadyAsync(cancellationToken);

            StatusMessage = $"FFmpeg 已就绪，正在拼接 {segments.Count} 段视频...";
            var progressReporter = new Progress<FFmpegProgressUpdate>(update => UpdateVideoJoinProgress(update, segments.Count));
            var exportResult = await _videoJoinWorkflowService.ExportAsync(request, progressReporter, cancellationToken);
            var result = exportResult.ExecutionResult;

            if (result.WasSuccessful && File.Exists(exportResult.Request.OutputPath))
            {
                StatusMessage =
                    $"视频拼接完成：{Path.GetFileName(exportResult.Request.OutputPath)}。预设分辨率来源：{presetTrackItem.SourceName} · {presetTrackItem.ResolutionText}；较小分辨率视频：{GetSmallerResolutionStrategyLabel()}；较大分辨率视频：{GetLargerResolutionStrategyLabel()}。";
                TryRevealVideoJoinOutputFile(exportResult.Request.OutputPath);
                return;
            }

            StatusMessage = result.WasCancelled
                ? "已取消视频拼接任务。"
                : $"视频拼接失败：{ExtractFriendlyVideoJoinFailureMessage(result)}";
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

        if (_audioJoinWorkflowService is null)
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
            StatusMessage = "正在检查音频拼接素材并准备输出参数...";

            EnsureAudioOutputDirectoryExists();

            var activeTrackItems = _audioJoinAudioTrackItems
                .Where(trackItem => trackItem.IsSourceAvailable)
                .ToArray();
            var presetTrackItem = GetEffectiveAudioParameterPresetItem() ?? activeTrackItems[0];
            var presetTrackIndex = Array.FindIndex(activeTrackItems, trackItem => trackItem.TrackId == presetTrackItem.TrackId);
            if (presetTrackIndex < 0)
            {
                throw new InvalidOperationException("无法定位当前音频参数预设片段。");
            }

            var segments = await BuildAudioJoinSegmentsAsync(activeTrackItems, cancellationToken);
            var presetSegment = segments[presetTrackIndex];
            var plannedOutputPath = CreatePlannedAudioJoinOutputPath(presetTrackItem.SourcePath);
            var request = new AudioJoinExportRequest(
                segments,
                plannedOutputPath,
                SelectedAudioJoinOutputFormat,
                presetSegment.SampleRate,
                presetSegment.Bitrate);

            StatusMessage = "正在准备 FFmpeg 运行时...";
            await _audioJoinWorkflowService.EnsureRuntimeReadyAsync(cancellationToken);

            StatusMessage = $"FFmpeg 已就绪，正在拼接 {segments.Count} 段音频...";
            var progressReporter = new Progress<FFmpegProgressUpdate>(update => UpdateAudioJoinProgress(update, segments.Count));
            var exportResult = await _audioJoinWorkflowService.ExportAsync(request, progressReporter, cancellationToken);
            var result = exportResult.ExecutionResult;

            if (result.WasSuccessful && File.Exists(exportResult.Request.OutputPath))
            {
                StatusMessage =
                    $"音频拼接完成：{Path.GetFileName(exportResult.Request.OutputPath)}。参数基准来源：{presetTrackItem.SourceName} · {BuildAudioJoinPresetSummaryText(presetSegment)}。";
                TryRevealVideoJoinOutputFile(exportResult.Request.OutputPath);
                return;
            }

            StatusMessage = result.WasCancelled
                ? "已取消音频拼接任务。"
                : $"音频拼接失败：{ExtractFriendlyVideoJoinFailureMessage(result)}";
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

    private async Task<TimeSpan> ResolveTrackDurationAsync(
        string sourcePath,
        string fallbackDurationText,
        CancellationToken cancellationToken)
    {
        if (_mediaInfoService is not null && !string.IsNullOrWhiteSpace(sourcePath))
        {
            try
            {
                var loadResult = _mediaInfoService.TryGetCachedDetails(sourcePath, out var cachedSnapshot)
                    ? MediaDetailsLoadResult.Success(cachedSnapshot)
                    : await _mediaInfoService.GetMediaDetailsAsync(sourcePath, cancellationToken);

                if (loadResult.IsSuccess &&
                    loadResult.Snapshot?.MediaDuration is { } mediaDuration &&
                    mediaDuration > TimeSpan.Zero)
                {
                    return mediaDuration;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                _logger?.Log(LogLevel.Warning, $"读取视频拼接素材时长失败：{sourcePath}", exception);
            }
        }

        return TimeSpan.TryParse(fallbackDurationText, out var parsedDuration)
            ? parsedDuration
            : TimeSpan.Zero;
    }

    private async Task<IReadOnlyList<VideoJoinSegment>> BuildVideoJoinSegmentsAsync(
        IReadOnlyList<TrackItem> activeTrackItems,
        CancellationToken cancellationToken)
    {
        var segments = new List<VideoJoinSegment>(activeTrackItems.Count);

        foreach (var trackItem in activeTrackItems)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var snapshot = await LoadMediaDetailsSnapshotAsync(trackItem.SourcePath, cancellationToken);
            if (snapshot is not null && !snapshot.HasVideoStream)
            {
                throw new InvalidOperationException($"{trackItem.SourceName} 不包含可拼接的视频流。");
            }

            if (!TryResolveVideoJoinResolution(snapshot, trackItem, out var width, out var height))
            {
                throw new InvalidOperationException($"无法读取 {trackItem.SourceName} 的分辨率信息。");
            }

            var duration = await ResolveTrackDurationAsync(trackItem.SourcePath, trackItem.DurationText, cancellationToken);
            if (duration <= TimeSpan.Zero)
            {
                throw new InvalidOperationException($"无法读取 {trackItem.SourceName} 的时长信息。");
            }

            var frameRate = TryResolveVideoJoinFrameRate(snapshot, out var resolvedFrameRate)
                ? resolvedFrameRate
                : 30d;

            segments.Add(new VideoJoinSegment(
                trackItem.SourcePath,
                trackItem.SourceName,
                width,
                height,
                frameRate,
                duration,
                snapshot?.HasAudioStream ?? true));
        }

        return segments;
    }

    private async Task<IReadOnlyList<AudioJoinSegment>> BuildAudioJoinSegmentsAsync(
        IReadOnlyList<TrackItem> activeTrackItems,
        CancellationToken cancellationToken)
    {
        var segments = new List<AudioJoinSegment>(activeTrackItems.Count);

        foreach (var trackItem in activeTrackItems)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var snapshot = await LoadMediaDetailsSnapshotAsync(trackItem.SourcePath, cancellationToken);
            if (snapshot is not null && !snapshot.HasAudioStream)
            {
                throw new InvalidOperationException($"{trackItem.SourceName} 不包含可拼接的音频流。");
            }

            var duration = await ResolveTrackDurationAsync(trackItem.SourcePath, trackItem.DurationText, cancellationToken);
            if (duration <= TimeSpan.Zero)
            {
                throw new InvalidOperationException($"无法读取 {trackItem.SourceName} 的时长信息。");
            }

            if (!TryResolveAudioJoinSampleRate(snapshot, trackItem, out var sampleRate))
            {
                throw new InvalidOperationException($"无法读取 {trackItem.SourceName} 的采样率信息。");
            }

            int? bitrate = TryResolveAudioJoinBitrate(snapshot, trackItem, out var resolvedBitrate)
                ? resolvedBitrate
                : null;

            segments.Add(new AudioJoinSegment(
                trackItem.SourcePath,
                trackItem.SourceName,
                duration,
                sampleRate,
                bitrate));
        }

        return segments;
    }

    private async Task<MediaDetailsSnapshot?> LoadMediaDetailsSnapshotAsync(string sourcePath, CancellationToken cancellationToken)
    {
        if (_mediaInfoService is null || string.IsNullOrWhiteSpace(sourcePath))
        {
            return null;
        }

        if (_mediaInfoService.TryGetCachedDetails(sourcePath, out var cachedSnapshot))
        {
            return cachedSnapshot;
        }

        var loadResult = await _mediaInfoService.GetMediaDetailsAsync(sourcePath, cancellationToken);
        return loadResult.IsSuccess ? loadResult.Snapshot : null;
    }

    private static bool TryResolveVideoJoinResolution(
        MediaDetailsSnapshot? snapshot,
        TrackItem trackItem,
        out int width,
        out int height)
    {
        var resolutionText = snapshot is null
            ? trackItem.ResolutionText
            : TryGetDetailFieldValue(snapshot.VideoFields, "分辨率") ??
              TryGetDetailFieldValue(snapshot.OverviewFields, "分辨率") ??
              trackItem.ResolutionText;

        return TryParseResolutionText(resolutionText, out width, out height);
    }

    private static bool TryResolveVideoJoinFrameRate(MediaDetailsSnapshot? snapshot, out double frameRate)
    {
        frameRate = 0d;
        if (snapshot is null)
        {
            return false;
        }

        var frameRateText = TryGetDetailFieldValue(snapshot.VideoFields, "帧率");
        if (string.IsNullOrWhiteSpace(frameRateText))
        {
            return false;
        }

        var numericText = new string(frameRateText
            .Trim()
            .TakeWhile(character => char.IsDigit(character) || character is '.')
            .ToArray());
        return double.TryParse(numericText, NumberStyles.Float, CultureInfo.InvariantCulture, out frameRate) &&
               frameRate > 0d;
    }

    private static bool TryResolveAudioJoinSampleRate(
        MediaDetailsSnapshot? snapshot,
        TrackItem trackItem,
        out int sampleRate)
    {
        var sampleRateText = snapshot is null
            ? trackItem.ResolutionText
            : TryGetDetailFieldValue(snapshot.AudioFields, "采样率") ??
              trackItem.ResolutionText;

        return TryParseSampleRateText(sampleRateText, out sampleRate);
    }

    private static bool TryResolveAudioJoinBitrate(
        MediaDetailsSnapshot? snapshot,
        TrackItem trackItem,
        out int bitrate)
    {
        var bitrateText = snapshot is null
            ? trackItem.ResolutionText
            : TryGetDetailFieldValue(snapshot.AudioFields, "音频码率") ??
              trackItem.ResolutionText;

        return TryParseBitrateText(bitrateText, out bitrate);
    }

    private static bool TryParseSampleRateText(string? sampleRateText, out int sampleRate)
    {
        sampleRate = 0;
        if (string.IsNullOrWhiteSpace(sampleRateText))
        {
            return false;
        }

        var segments = sampleRateText.Split(
            new[] { '·', '|', ',', ';', '，', '；' },
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var candidate = segments.FirstOrDefault(segment =>
            segment.Contains("Hz", StringComparison.OrdinalIgnoreCase));
        if (string.IsNullOrWhiteSpace(candidate))
        {
            candidate = sampleRateText;
        }

        var numericText = new string(candidate
            .Where(character => char.IsDigit(character) || character is '.')
            .ToArray());
        if (!double.TryParse(numericText, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || value <= 0d)
        {
            return false;
        }

        if (candidate.Contains("kHz", StringComparison.OrdinalIgnoreCase))
        {
            value *= 1_000d;
        }

        sampleRate = (int)Math.Round(value, MidpointRounding.AwayFromZero);
        return sampleRate > 0;
    }

    private static bool TryParseBitrateText(string? bitrateText, out int bitrate)
    {
        bitrate = 0;
        if (string.IsNullOrWhiteSpace(bitrateText))
        {
            return false;
        }

        var segments = bitrateText.Split(
            new[] { '·', '|', ',', ';', '，', '；' },
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var candidate = segments.FirstOrDefault(segment =>
            segment.Contains("bps", StringComparison.OrdinalIgnoreCase) ||
            segment.Contains("比特/秒", StringComparison.Ordinal));
        if (string.IsNullOrWhiteSpace(candidate))
        {
            candidate = bitrateText;
        }

        var numericText = new string(candidate
            .Where(character => char.IsDigit(character) || character is '.')
            .ToArray());
        if (!double.TryParse(numericText, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || value <= 0d)
        {
            return false;
        }

        if (candidate.Contains("Mbps", StringComparison.OrdinalIgnoreCase))
        {
            value *= 1_000_000d;
        }
        else if (candidate.Contains("kbps", StringComparison.OrdinalIgnoreCase))
        {
            value *= 1_000d;
        }

        bitrate = (int)Math.Round(value, MidpointRounding.AwayFromZero);
        return bitrate > 0;
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

    private static string BuildAudioJoinPresetSummaryText(AudioJoinSegment presetSegment)
    {
        var sampleRateText = FormatAudioJoinSampleRateText(presetSegment.SampleRate);
        var bitrateText = presetSegment.Bitrate is > 0
            ? FormatAudioJoinBitrateText(presetSegment.Bitrate.Value)
            : "自动码率";
        return $"{sampleRateText} · {bitrateText}";
    }

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
            PreferredMergeAudioJoinOutputDirectory = AudioJoinHasCustomOutputDirectory ? AudioJoinOutputDirectory : null
        });
    }

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
        GetEffectiveAudioJoinOutputBaseName(GetEffectiveAudioParameterPresetItem()?.SourcePath);

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
        var presetTrackItem = GetEffectiveAudioParameterPresetItem();
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
            _logger?.Log(LogLevel.Warning, $"解析音频拼接默认输出目录失败：{presetTrackItem.SourcePath}", exception);
            return null;
        }
    }

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
                resolutionText = isVideo ? ResolveResolutionText(snapshot) : ResolveAudioParameterText(snapshot);

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

    private static bool TryParseResolutionText(string? resolutionText, out int width, out int height)
    {
        width = 0;
        height = 0;
        if (string.IsNullOrWhiteSpace(resolutionText))
        {
            return false;
        }

        var normalizedText = resolutionText
            .Replace("×", "x", StringComparison.Ordinal)
            .Replace("X", "x", StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal);
        var parts = normalizedText.Split('x', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length == 2 &&
               int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out width) &&
               int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out height) &&
               width > 0 &&
               height > 0;
    }

    private static string ResolveResolutionText(MediaDetailsSnapshot snapshot)
    {
        var resolutionText = TryGetDetailFieldValue(snapshot.VideoFields, "分辨率") ??
                             TryGetDetailFieldValue(snapshot.OverviewFields, "分辨率");
        return string.IsNullOrWhiteSpace(resolutionText) ? "未知分辨率" : resolutionText;
    }

    private static string ResolveAudioParameterText(MediaDetailsSnapshot snapshot)
    {
        var sampleRateText = TryGetDetailFieldValue(snapshot.AudioFields, "采样率");
        var bitrateText = TryGetDetailFieldValue(snapshot.AudioFields, "音频码率");
        if (string.IsNullOrWhiteSpace(sampleRateText) && string.IsNullOrWhiteSpace(bitrateText))
        {
            return "未知音频参数";
        }

        if (string.IsNullOrWhiteSpace(sampleRateText))
        {
            return bitrateText!;
        }

        if (string.IsNullOrWhiteSpace(bitrateText))
        {
            return sampleRateText;
        }

        return $"{sampleRateText} · {bitrateText}";
    }

    private static string? TryGetDetailFieldValue(IReadOnlyList<MediaDetailField> fields, string label)
    {
        foreach (var field in fields)
        {
            if (string.Equals(field.Label, label, StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(field.Value))
            {
                return field.Value;
            }
        }

        return null;
    }

    private static TrackItem CreateTrackItem(MediaItem mediaItem, int index, bool isSourceAvailable)
    {
        ArgumentNullException.ThrowIfNull(mediaItem);
        var visualWidth = mediaItem.IsVideo
            ? Math.Clamp(164d + (mediaItem.DurationSeconds * 2.2d), 248d, 360d)
            : Math.Clamp(148d + (mediaItem.DurationSeconds * 1.8d), 220d, 320d);

        return new TrackItem(
            mediaItem.FileName,
            mediaItem.SourcePath,
            mediaItem.DurationText,
            mediaItem.ResolutionText,
            visualWidth,
            mediaItem.IsVideo,
            index,
            isSourceAvailable);
    }

    private void SetMergeMode(MergeWorkspaceMode mergeMode)
    {
        if (_selectedMergeMode == mergeMode)
        {
            return;
        }

        if (IsVideoJoinProcessing)
        {
            StatusMessage = "视频拼接任务处理中，若需切换模式，请先取消当前任务。";
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

        StatusMessage = mergeMode switch
        {
            MergeWorkspaceMode.VideoJoin => "已切换到视频拼接模式。",
            MergeWorkspaceMode.AudioJoin => "已切换到音频拼接模式。",
            _ => "已切换到音视频合成模式。"
        };

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
        RefreshTrackCollection(_audioVideoComposeVideoTrackItems, supportsVideoPreset: false);
        RefreshTrackCollection(_audioVideoComposeAudioTrackItems, supportsVideoPreset: false);
        RaiseTrackStatePropertiesChanged();
    }

    private void RefreshTrackCollection(
        ObservableCollection<TrackItem> trackItems,
        bool supportsVideoPreset)
    {
        var presetTrackItem = ReferenceEquals(trackItems, _videoJoinVideoTrackItems)
            ? GetEffectiveVideoResolutionPresetItem()
            : ReferenceEquals(trackItems, _audioJoinAudioTrackItems)
                ? GetEffectiveAudioParameterPresetItem()
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
        rejectionMessage = string.Empty;

        switch (_selectedMergeMode)
        {
            case MergeWorkspaceMode.VideoJoin:
                if (mediaItem.IsAudio)
                {
                    trackItems = _emptyTrackItems;
                    rejectionMessage = "当前是视频拼接模式，请选择视频素材加入视频轨道。";
                    return false;
                }

                trackItems = _videoJoinVideoTrackItems;
                return true;

            case MergeWorkspaceMode.AudioJoin:
                if (mediaItem.IsVideo)
                {
                    trackItems = _emptyTrackItems;
                    rejectionMessage = "当前是音频拼接模式，请选择音频素材加入音频轨道。";
                    return false;
                }

                trackItems = _audioJoinAudioTrackItems;
                return true;

            default:
                trackItems = mediaItem.IsVideo
                    ? _audioVideoComposeVideoTrackItems
                    : _audioVideoComposeAudioTrackItems;
                return true;
        }
    }

    private ObservableCollection<TrackItem> GetVideoTrackItems(MergeWorkspaceMode mergeMode) =>
        mergeMode switch
        {
            MergeWorkspaceMode.VideoJoin => _videoJoinVideoTrackItems,
            MergeWorkspaceMode.AudioVideoCompose => _audioVideoComposeVideoTrackItems,
            _ => _emptyTrackItems
        };

    private ObservableCollection<TrackItem> GetAudioTrackItems(MergeWorkspaceMode mergeMode) =>
        mergeMode switch
        {
            MergeWorkspaceMode.AudioJoin => _audioJoinAudioTrackItems,
            MergeWorkspaceMode.AudioVideoCompose => _audioVideoComposeAudioTrackItems,
            _ => _emptyTrackItems
        };

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
