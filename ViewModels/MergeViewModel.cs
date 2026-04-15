using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
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
    private readonly ObservableCollection<string> _outputFormats;
    private readonly AsyncRelayCommand _importFilesCommand;
    private readonly AsyncRelayCommand _browseOutputPathCommand;
    private readonly RelayCommand _exportCommand;
    private readonly IFilePickerService? _filePickerService;
    private readonly IMediaInfoService? _mediaInfoService;
    private readonly IUserPreferencesService? _userPreferencesService;
    private readonly ILogger? _logger;
    private readonly HashSet<string> _supportedVideoInputFileTypes;
    private readonly HashSet<string> _supportedAudioInputFileTypes;
    private readonly IReadOnlyList<string> _supportedImportFileTypes;

    private string _selectedOutputFormat;
    private string _outputPath;
    private string _statusMessage;
    private string _modeMismatchWarningMessage;
    private bool _isModeMismatchWarningVisible;
    private MergeWorkspaceMode _selectedMergeMode;
    private ResolutionStrategy _selectedResolutionStrategy;
    private DurationStrategy _selectedDurationStrategy;
    private TrackItem? _manualVideoResolutionPresetItem;

    public MergeViewModel(
        IFilePickerService? filePickerService = null,
        IMediaInfoService? mediaInfoService = null,
        IUserPreferencesService? userPreferencesService = null,
        ApplicationConfiguration? configuration = null,
        ILogger? logger = null)
    {
        var effectiveConfiguration = configuration ?? new ApplicationConfiguration();

        _filePickerService = filePickerService;
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
        _outputFormats = new ObservableCollection<string> { "MP4", "MKV" };

        _selectedOutputFormat = _outputFormats[0];
        _outputPath = string.Empty;
        _statusMessage = "请先导入视频或音频文件，再添加到对应轨道。";
        _modeMismatchWarningMessage = string.Empty;
        _selectedMergeMode = ResolvePreferredMergeMode(
            _userPreferencesService?.Load().PreferredMergeWorkspaceMode
            ?? MergeWorkspaceMode.AudioVideoCompose);
        _selectedResolutionStrategy = ResolutionStrategy.MatchFirstVideo;
        _selectedDurationStrategy = DurationStrategy.VideoPriority;
        _importFilesCommand = new AsyncRelayCommand(ImportFilesAsync);
        _browseOutputPathCommand = new AsyncRelayCommand(BrowseOutputPathAsync);
        _exportCommand = new RelayCommand(PrepareExportPreview);

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

    public ObservableCollection<string> OutputFormats => _outputFormats;

    public ICommand ImportFilesCommand => _importFilesCommand;

    public ICommand BrowseOutputPathCommand => _browseOutputPathCommand;

    public ICommand ExportCommand => _exportCommand;

    public string SelectedOutputFormat
    {
        get => _selectedOutputFormat;
        set
        {
            if (!string.IsNullOrWhiteSpace(value) && SetProperty(ref _selectedOutputFormat, value))
            {
                StatusMessage = $"输出格式已切换为 {value}。";
            }
        }
    }

    public string OutputPath
    {
        get => _outputPath;
        set => SetProperty(ref _outputPath, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

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

    public bool IsMatchFirstVideoResolutionSelected
    {
        get => _selectedResolutionStrategy == ResolutionStrategy.MatchFirstVideo;
        set
        {
            if (value)
            {
                SetResolutionStrategy(ResolutionStrategy.MatchFirstVideo);
            }
        }
    }

    public bool IsPadWithBlackBarsSelected
    {
        get => _selectedResolutionStrategy == ResolutionStrategy.PadWithBlackBars;
        set
        {
            if (value)
            {
                SetResolutionStrategy(ResolutionStrategy.PadWithBlackBars);
            }
        }
    }

    public bool IsStretchToFillSelected
    {
        get => _selectedResolutionStrategy == ResolutionStrategy.StretchToFill;
        set
        {
            if (value)
            {
                SetResolutionStrategy(ResolutionStrategy.StretchToFill);
            }
        }
    }

    public bool IsVideoPrioritySelected
    {
        get => _selectedDurationStrategy == DurationStrategy.VideoPriority;
        set
        {
            if (value)
            {
                SetDurationStrategy(DurationStrategy.VideoPriority);
            }
        }
    }

    public bool IsAudioPrioritySelected
    {
        get => _selectedDurationStrategy == DurationStrategy.AudioPriority;
        set
        {
            if (value)
            {
                SetDurationStrategy(DurationStrategy.AudioPriority);
            }
        }
    }

    public bool IsAutoLoopSelected
    {
        get => _selectedDurationStrategy == DurationStrategy.AutoLoop;
        set
        {
            if (value)
            {
                SetDurationStrategy(DurationStrategy.AutoLoop);
            }
        }
    }

    public string TimelineHintText => _selectedMergeMode switch
    {
        MergeWorkspaceMode.VideoJoin => "当前为视频拼接模式，仅可将视频素材添加到视频轨道。",
        MergeWorkspaceMode.AudioJoin => "当前为音频拼接模式，仅可将音频素材添加到音频轨道。",
        _ => "当前为音视频合成模式，素材会自动加入对应轨道。"
    };

    public string VideoTrackSummaryText => ActiveVideoTrackItems.Count == 0
        ? "未添加片段"
        : $"{ActiveVideoTrackItems.Count} 个片段";

    public string AudioTrackSummaryText => ActiveAudioTrackItems.Count == 0
        ? "未添加片段"
        : $"{ActiveAudioTrackItems.Count} 个片段";

    public Visibility MediaItemsEmptyVisibility => _mediaItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    public Visibility VideoTrackEmptyVisibility => ActiveVideoTrackItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    public Visibility AudioTrackEmptyVisibility => ActiveAudioTrackItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    public Visibility VideoJoinTimelineVisibility =>
        _selectedMergeMode == MergeWorkspaceMode.VideoJoin ? Visibility.Visible : Visibility.Collapsed;

    public Visibility StandardTimelineVisibility =>
        _selectedMergeMode == MergeWorkspaceMode.VideoJoin ? Visibility.Collapsed : Visibility.Visible;

    public string VideoResolutionPresetSummaryText
    {
        get
        {
            if (_videoJoinVideoTrackItems.Count == 0)
            {
                return "添加视频片段后，将默认以首段可用视频作为分辨率基准。";
            }

            var presetTrackItem = GetEffectiveVideoResolutionPresetItem();
            if (presetTrackItem is null)
            {
                return "当前暂无可用的视频片段作为分辨率基准。";
            }

            return _manualVideoResolutionPresetItem is not null &&
                   ReferenceEquals(_manualVideoResolutionPresetItem, presetTrackItem)
                ? $"当前分辨率预设：{presetTrackItem.SequenceNumberText} · {presetTrackItem.SourceName}"
                : $"当前默认以首段可用视频为分辨率基准：{presetTrackItem.SequenceNumberText} · {presetTrackItem.SourceName}";
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

        if (!_videoJoinVideoTrackItems.Contains(trackItem))
        {
            return;
        }

        var removedClipName = trackItem.SourceName;
        var removedPresetClip = ReferenceEquals(GetEffectiveVideoResolutionPresetItem(), trackItem);
        _videoJoinVideoTrackItems.Remove(trackItem);

        var fallbackPresetTrackItem = GetEffectiveVideoResolutionPresetItem();
        StatusMessage = removedPresetClip && fallbackPresetTrackItem is not null
            ? $"{removedClipName} 已从视频轨道移除，分辨率基准已切换为 {fallbackPresetTrackItem.SequenceNumberText} 号片段。"
            : $"{removedClipName} 已从视频轨道移除。";
    }

    public void SetVideoResolutionPreset(TrackItem trackItem)
    {
        ArgumentNullException.ThrowIfNull(trackItem);

        if (!_videoJoinVideoTrackItems.Contains(trackItem))
        {
            return;
        }

        if (!trackItem.CanSetAsResolutionPreset)
        {
            StatusMessage = "当前片段引用的源素材已失效，无法设为分辨率预设。";
            return;
        }

        _manualVideoResolutionPresetItem = trackItem;
        RefreshTrackCollection(_videoJoinVideoTrackItems, supportsVideoPreset: true);
        RaiseTrackStatePropertiesChanged();
        StatusMessage = $"{trackItem.SequenceNumberText} 号片段已设为分辨率预设。";
    }

    public string BuildExportPreviewMessage()
    {
        var activeVideoTrackCount = ActiveVideoTrackItems.Count(trackItem => trackItem.IsSourceAvailable);
        var activeAudioTrackCount = ActiveAudioTrackItems.Count(trackItem => trackItem.IsSourceAvailable);
        var invalidTrackCount = ActiveVideoTrackItems.Count(trackItem => !trackItem.IsSourceAvailable) +
                                ActiveAudioTrackItems.Count(trackItem => !trackItem.IsSourceAvailable);

        if (activeVideoTrackCount + activeAudioTrackCount == 0)
        {
            return invalidTrackCount > 0
                ? "当前轨道中的片段均已标记为失效。\n\n请重新添加对应素材，或先移除失效片段后再进行导出。"
                : "当前还未向轨道添加任何片段。\n\n导出入口已准备就绪，实际合并能力将在后续阶段接入。";
        }

        return _selectedMergeMode switch
        {
            MergeWorkspaceMode.VideoJoin =>
                $"当前视频拼接工作区包含 {activeVideoTrackCount} 个可用视频片段，另有 {invalidTrackCount} 个失效片段。\n\n界面已保留输出格式、输出路径、分辨率策略和时长策略等入口，实际导出能力将在后续阶段接入。",
            MergeWorkspaceMode.AudioJoin =>
                $"当前音频拼接工作区包含 {activeAudioTrackCount} 个可用音频片段，另有 {invalidTrackCount} 个失效片段。\n\n界面已保留输出格式、输出路径、分辨率策略和时长策略等入口，实际导出能力将在后续阶段接入。",
            _ =>
                $"当前音视频合成工作区包含 {activeVideoTrackCount} 个可用视频片段、{activeAudioTrackCount} 个可用音频片段，另有 {invalidTrackCount} 个失效片段。\n\n界面已保留输出格式、输出路径、分辨率策略和时长策略等入口，实际导出能力将在后续阶段接入。"
        };
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

    private async Task BrowseOutputPathAsync()
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

            OutputPath = selectedFolder;
            StatusMessage = $"已将输出目录设置为：{selectedFolder}";
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

    private void PrepareExportPreview()
    {
        StatusMessage = "已生成导出预览提示，实际合并导出能力将在后续阶段接入。";
    }

    private async Task<MediaItem> CreateMediaItemAsync(string filePath)
    {
        var fileName = Path.GetFileName(filePath);
        var isVideo = IsVideoByExtension(filePath);
        var durationText = "未知时长";
        var durationSeconds = 0;
        var resolutionText = isVideo ? "未知分辨率" : "音频素材";

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
                resolutionText = isVideo ? ResolveResolutionText(snapshot) : "音频素材";

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

    private static string ResolveResolutionText(MediaDetailsSnapshot snapshot)
    {
        var resolutionText = TryGetDetailFieldValue(snapshot.VideoFields, "分辨率") ??
                             TryGetDetailFieldValue(snapshot.OverviewFields, "分辨率");
        return string.IsNullOrWhiteSpace(resolutionText) ? "未知分辨率" : resolutionText;
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

        _selectedMergeMode = mergeMode;
        OnPropertyChanged(nameof(IsVideoJoinModeSelected));
        OnPropertyChanged(nameof(IsAudioJoinModeSelected));
        OnPropertyChanged(nameof(IsAudioVideoComposeModeSelected));
        OnPropertyChanged(nameof(TimelineHintText));
        OnPropertyChanged(nameof(VideoTrackItems));
        OnPropertyChanged(nameof(AudioTrackItems));
        OnPropertyChanged(nameof(VideoTrackEmptyText));
        OnPropertyChanged(nameof(AudioTrackEmptyText));
        OnPropertyChanged(nameof(VideoJoinTimelineVisibility));
        OnPropertyChanged(nameof(StandardTimelineVisibility));
        RaiseTrackStatePropertiesChanged();
        SetModeMismatchWarningVisibility(false, string.Empty);

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

    private void SetResolutionStrategy(ResolutionStrategy resolutionStrategy)
    {
        if (_selectedResolutionStrategy == resolutionStrategy)
        {
            return;
        }

        _selectedResolutionStrategy = resolutionStrategy;
        OnPropertyChanged(nameof(IsMatchFirstVideoResolutionSelected));
        OnPropertyChanged(nameof(IsPadWithBlackBarsSelected));
        OnPropertyChanged(nameof(IsStretchToFillSelected));
    }

    private void SetDurationStrategy(DurationStrategy durationStrategy)
    {
        if (_selectedDurationStrategy == durationStrategy)
        {
            return;
        }

        _selectedDurationStrategy = durationStrategy;
        OnPropertyChanged(nameof(IsVideoPrioritySelected));
        OnPropertyChanged(nameof(IsAudioPrioritySelected));
        OnPropertyChanged(nameof(IsAutoLoopSelected));
    }

    private void OnMediaItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        SynchronizeTrackCollectionsWithMediaSources();
        OnPropertyChanged(nameof(MediaItemsEmptyVisibility));
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
        if (supportsVideoPreset &&
            _manualVideoResolutionPresetItem is not null &&
            (!_videoJoinVideoTrackItems.Contains(_manualVideoResolutionPresetItem) ||
             !_manualVideoResolutionPresetItem.IsSourceAvailable))
        {
            _manualVideoResolutionPresetItem = null;
        }

        var presetTrackItem = supportsVideoPreset ? GetEffectiveVideoResolutionPresetItem() : null;
        for (var index = 0; index < trackItems.Count; index++)
        {
            var trackItem = trackItems[index];
            trackItem.SequenceNumber = index + 1;
            trackItem.IsResolutionPreset = supportsVideoPreset && ReferenceEquals(trackItem, presetTrackItem);
        }
    }

    private TrackItem? GetEffectiveVideoResolutionPresetItem()
    {
        if (_manualVideoResolutionPresetItem is not null &&
            _videoJoinVideoTrackItems.Contains(_manualVideoResolutionPresetItem) &&
            _manualVideoResolutionPresetItem.IsSourceAvailable)
        {
            return _manualVideoResolutionPresetItem;
        }

        return _videoJoinVideoTrackItems.FirstOrDefault(trackItem => trackItem.IsSourceAvailable);
    }

    private void RaiseTrackStatePropertiesChanged()
    {
        OnPropertyChanged(nameof(VideoTrackSummaryText));
        OnPropertyChanged(nameof(AudioTrackSummaryText));
        OnPropertyChanged(nameof(VideoTrackEmptyVisibility));
        OnPropertyChanged(nameof(AudioTrackEmptyVisibility));
        OnPropertyChanged(nameof(VideoResolutionPresetSummaryText));
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

    private enum ResolutionStrategy
    {
        MatchFirstVideo,
        PadWithBlackBars,
        StretchToFill
    }

    private enum DurationStrategy
    {
        VideoPriority,
        AudioPriority,
        AutoLoop
    }
}
