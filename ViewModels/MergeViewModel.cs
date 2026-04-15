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
    private readonly ObservableCollection<TrackItem> _videoTrackItems;
    private readonly ObservableCollection<TrackItem> _audioTrackItems;
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
        _videoTrackItems = new ObservableCollection<TrackItem>();
        _audioTrackItems = new ObservableCollection<TrackItem>();
        _outputFormats = new ObservableCollection<string> { "MP4", "MKV" };
        _selectedOutputFormat = _outputFormats[0];
        _outputPath = string.Empty;
        _statusMessage = "请先导入视频或音频文件，再添加到对应轨道。";
        _selectedMergeMode = ResolvePreferredMergeMode(
            _userPreferencesService?.Load().PreferredMergeWorkspaceMode
            ?? MergeWorkspaceMode.AudioVideoCompose);
        _selectedResolutionStrategy = ResolutionStrategy.MatchFirstVideo;
        _selectedDurationStrategy = DurationStrategy.VideoPriority;
        _importFilesCommand = new AsyncRelayCommand(ImportFilesAsync);
        _browseOutputPathCommand = new AsyncRelayCommand(BrowseOutputPathAsync);
        _exportCommand = new RelayCommand(PrepareExportPreview);

        _mediaItems.CollectionChanged += OnMediaItemsChanged;
        _videoTrackItems.CollectionChanged += OnTrackItemsChanged;
        _audioTrackItems.CollectionChanged += OnTrackItemsChanged;
    }

    public ObservableCollection<MediaItem> MediaItems => _mediaItems;

    public ObservableCollection<TrackItem> VideoTrackItems => _videoTrackItems;

    public ObservableCollection<TrackItem> AudioTrackItems => _audioTrackItems;

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

    public string VideoTrackSummaryText => _videoTrackItems.Count == 0
        ? "未添加片段"
        : $"{_videoTrackItems.Count} 个片段";

    public string AudioTrackSummaryText => _audioTrackItems.Count == 0
        ? "未添加片段"
        : $"{_audioTrackItems.Count} 个片段";

    public Visibility MediaItemsEmptyVisibility => _mediaItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    public Visibility VideoTrackEmptyVisibility => _videoTrackItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    public Visibility AudioTrackEmptyVisibility => _audioTrackItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    public Visibility VideoJoinTimelineVisibility =>
        _selectedMergeMode == MergeWorkspaceMode.VideoJoin ? Visibility.Visible : Visibility.Collapsed;

    public Visibility StandardTimelineVisibility =>
        _selectedMergeMode == MergeWorkspaceMode.VideoJoin ? Visibility.Collapsed : Visibility.Visible;

    public string VideoResolutionPresetSummaryText
    {
        get
        {
            var presetTrackItem = GetEffectiveVideoResolutionPresetItem();
            if (presetTrackItem is null)
            {
                return "添加视频片段后，将默认以首段视频作为分辨率基准。";
            }

            return _manualVideoResolutionPresetItem is not null &&
                   ReferenceEquals(_manualVideoResolutionPresetItem, presetTrackItem)
                ? $"当前分辨率预设：{presetTrackItem.SequenceNumberText} · {presetTrackItem.SourceName}"
                : $"当前默认以首段视频为分辨率基准：{presetTrackItem.SequenceNumberText} · {presetTrackItem.SourceName}";
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

        switch (_selectedMergeMode)
        {
            case MergeWorkspaceMode.VideoJoin when mediaItem.IsAudio:
                StatusMessage = "当前是视频拼接模式，请选择视频素材加入视频轨道。";
                return;
            case MergeWorkspaceMode.AudioJoin when mediaItem.IsVideo:
                StatusMessage = "当前是音频拼接模式，请选择音频素材加入音频轨道。";
                return;
        }

        var trackItems = mediaItem.IsVideo ? _videoTrackItems : _audioTrackItems;
        trackItems.Add(CreateTrackItem(mediaItem, trackItems.Count + 1));

        StatusMessage = mediaItem.IsVideo
            ? $"{mediaItem.FileName} 已加入视频轨道。"
            : $"{mediaItem.FileName} 已加入音频轨道。";
    }

    public void RemoveMediaItem(MediaItem mediaItem)
    {
        ArgumentNullException.ThrowIfNull(mediaItem);

        if (!_mediaItems.Remove(mediaItem))
        {
            return;
        }

        StatusMessage = $"已从素材列表移除 {mediaItem.FileName}。";
    }

    public void RemoveVideoTrackItem(TrackItem trackItem)
    {
        ArgumentNullException.ThrowIfNull(trackItem);

        if (!_videoTrackItems.Contains(trackItem))
        {
            return;
        }

        var removedClipName = trackItem.SourceName;
        var removedPresetClip = trackItem.IsResolutionPreset;
        _videoTrackItems.Remove(trackItem);

        var fallbackPresetTrackItem = GetEffectiveVideoResolutionPresetItem();
        StatusMessage = removedPresetClip && fallbackPresetTrackItem is not null
            ? $"{removedClipName} 已从视频轨道移除，分辨率基准已切换为 {fallbackPresetTrackItem.SequenceNumberText} 号片段。"
            : $"{removedClipName} 已从视频轨道移除。";
    }

    public void SetVideoResolutionPreset(TrackItem trackItem)
    {
        ArgumentNullException.ThrowIfNull(trackItem);

        if (!_videoTrackItems.Contains(trackItem))
        {
            return;
        }

        _manualVideoResolutionPresetItem = trackItem;
        RefreshVideoTrackPresentation();
        StatusMessage = $"{trackItem.SequenceNumberText} 号片段已设为分辨率预设。";
    }

    public void ApplyVideoTrackOrdering(IReadOnlyList<TrackItem> orderedTrackItems)
    {
        ArgumentNullException.ThrowIfNull(orderedTrackItems);

        if (orderedTrackItems.Count != _videoTrackItems.Count ||
            orderedTrackItems.Any(trackItem => !_videoTrackItems.Contains(trackItem)))
        {
            RefreshVideoTrackPresentation();
            return;
        }

        for (var targetIndex = 0; targetIndex < orderedTrackItems.Count; targetIndex++)
        {
            var trackItem = orderedTrackItems[targetIndex];
            var currentIndex = _videoTrackItems.IndexOf(trackItem);
            if (currentIndex >= 0 && currentIndex != targetIndex)
            {
                _videoTrackItems.Move(currentIndex, targetIndex);
            }
        }

        RefreshVideoTrackPresentation();
    }

    public string BuildExportPreviewMessage()
    {
        var totalTrackItems = _videoTrackItems.Count + _audioTrackItems.Count;

        if (totalTrackItems == 0)
        {
            return "当前还未向轨道添加任何片段。\n\n导出入口已准备就绪，实际合并能力将在后续阶段接入。";
        }

        return $"当前轨道中包含 {_videoTrackItems.Count} 个视频片段、{_audioTrackItems.Count} 个音频片段。\n\n界面已保留输出格式、输出路径、分辨率策略和时长策略等入口，实际导出能力将在后续阶段接入。";
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
                    .Select(Path.GetFullPath),
                StringComparer.OrdinalIgnoreCase);

            var addedCount = 0;
            var duplicateCount = 0;

            foreach (var selectedFile in selectedFiles)
            {
                if (string.IsNullOrWhiteSpace(selectedFile))
                {
                    continue;
                }

                var filePath = Path.GetFullPath(selectedFile);
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

        if (_mediaInfoService is null)
        {
            return new MediaItem(fileName, durationText, durationSeconds, isVideo, filePath);
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

        return new MediaItem(fileName, durationText, durationSeconds, isVideo, filePath);
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

    private static TrackItem CreateTrackItem(MediaItem mediaItem, int index)
    {
        ArgumentNullException.ThrowIfNull(mediaItem);
        var visualWidth = Math.Clamp(96d + (mediaItem.DurationSeconds * 4d), 120d, 280d);
        return new TrackItem(
            mediaItem.FileName,
            mediaItem.DurationText,
            visualWidth,
            mediaItem.IsVideo,
            index);
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
        OnPropertyChanged(nameof(VideoTrackEmptyText));
        OnPropertyChanged(nameof(AudioTrackEmptyText));
        OnPropertyChanged(nameof(VideoJoinTimelineVisibility));
        OnPropertyChanged(nameof(StandardTimelineVisibility));
        OnPropertyChanged(nameof(VideoResolutionPresetSummaryText));

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
        OnPropertyChanged(nameof(MediaItemsEmptyVisibility));
    }

    private void OnTrackItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RefreshVideoTrackPresentation();
        OnPropertyChanged(nameof(AudioTrackSummaryText));
        OnPropertyChanged(nameof(AudioTrackEmptyVisibility));
    }

    private TrackItem? GetEffectiveVideoResolutionPresetItem()
    {
        if (_manualVideoResolutionPresetItem is not null &&
            _videoTrackItems.Contains(_manualVideoResolutionPresetItem))
        {
            return _manualVideoResolutionPresetItem;
        }

        return _videoTrackItems.FirstOrDefault();
    }

    private void RefreshVideoTrackPresentation()
    {
        if (_manualVideoResolutionPresetItem is not null &&
            !_videoTrackItems.Contains(_manualVideoResolutionPresetItem))
        {
            _manualVideoResolutionPresetItem = null;
        }

        for (var index = 0; index < _videoTrackItems.Count; index++)
        {
            var trackItem = _videoTrackItems[index];
            trackItem.SequenceNumber = index + 1;
            trackItem.IsResolutionPreset = false;
        }

        var presetTrackItem = GetEffectiveVideoResolutionPresetItem();
        if (presetTrackItem is not null)
        {
            presetTrackItem.IsResolutionPreset = true;
        }

        OnPropertyChanged(nameof(VideoTrackSummaryText));
        OnPropertyChanged(nameof(VideoTrackEmptyVisibility));
        OnPropertyChanged(nameof(VideoResolutionPresetSummaryText));
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
