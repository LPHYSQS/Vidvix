using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
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
    private readonly AsyncRelayCommand _importFilesCommand;
    private readonly AsyncRelayCommand _browseOutputDirectoryCommand;
    private readonly RelayCommand _clearOutputDirectoryCommand;
    private readonly AsyncRelayCommand _startVideoJoinProcessingCommand;
    private readonly RelayCommand _cancelVideoJoinProcessingCommand;
    private readonly IFilePickerService? _filePickerService;
    private readonly IMediaInfoService? _mediaInfoService;
    private readonly IUserPreferencesService? _userPreferencesService;
    private readonly ILogger? _logger;
    private readonly HashSet<string> _supportedVideoInputFileTypes;
    private readonly HashSet<string> _supportedAudioInputFileTypes;
    private readonly IReadOnlyList<string> _supportedImportFileTypes;
    private CancellationTokenSource? _videoJoinProcessingCancellationSource;

    private OutputFormatOption? _selectedOutputFormat;
    private string _outputDirectory;
    private string _statusMessage;
    private string _modeMismatchWarningMessage;
    private bool _isModeMismatchWarningVisible;
    private bool _isVideoJoinProcessing;
    private MergeWorkspaceMode _selectedMergeMode;
    private MergeSmallerResolutionStrategy _selectedSmallerResolutionStrategy;
    private MergeLargerResolutionStrategy _selectedLargerResolutionStrategy;
    private TrackItem? _manualVideoResolutionPresetItem;

    public MergeViewModel(
        IFilePickerService? filePickerService = null,
        IMediaInfoService? mediaInfoService = null,
        IUserPreferencesService? userPreferencesService = null,
        ApplicationConfiguration? configuration = null,
        ILogger? logger = null)
    {
        var effectiveConfiguration = configuration ?? new ApplicationConfiguration();
        var preferences = userPreferencesService?.Load() ?? new UserPreferences();

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
        _videoJoinOutputFormats = effectiveConfiguration.SupportedVideoOutputFormats;

        _selectedOutputFormat = ResolvePreferredVideoJoinOutputFormat(preferences.PreferredMergeVideoJoinOutputFormatExtension);
        _outputDirectory = NormalizeOutputDirectory(preferences.PreferredMergeVideoJoinOutputDirectory);
        _statusMessage = "请先导入视频或音频素材，再将它们添加到对应轨道。";
        _modeMismatchWarningMessage = string.Empty;
        _selectedMergeMode = ResolvePreferredMergeMode(preferences.PreferredMergeWorkspaceMode);
        _selectedSmallerResolutionStrategy = ResolvePreferredMergeSmallerResolutionStrategy(preferences.PreferredMergeSmallerResolutionStrategy);
        _selectedLargerResolutionStrategy = ResolvePreferredMergeLargerResolutionStrategy(preferences.PreferredMergeLargerResolutionStrategy);

        _importFilesCommand = new AsyncRelayCommand(ImportFilesAsync, () => !IsVideoJoinProcessing);
        _browseOutputDirectoryCommand = new AsyncRelayCommand(BrowseOutputDirectoryAsync, CanEditVideoJoinOutputSettings);
        _clearOutputDirectoryCommand = new RelayCommand(ClearOutputDirectory, CanClearOutputDirectory);
        _startVideoJoinProcessingCommand = new AsyncRelayCommand(StartVideoJoinProcessingAsync, CanStartVideoJoinProcessing);
        _cancelVideoJoinProcessingCommand = new RelayCommand(CancelVideoJoinProcessing, () => IsVideoJoinProcessing);

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

    public ICommand ImportFilesCommand => _importFilesCommand;

    public ICommand BrowseOutputDirectoryCommand => _browseOutputDirectoryCommand;

    public ICommand ClearOutputDirectoryCommand => _clearOutputDirectoryCommand;

    public ICommand StartVideoJoinProcessingCommand => _startVideoJoinProcessingCommand;

    public ICommand CancelVideoJoinProcessingCommand => _cancelVideoJoinProcessingCommand;

    public OutputFormatOption SelectedOutputFormat
    {
        get => _selectedOutputFormat ?? _videoJoinOutputFormats.First();
        set
        {
            if (value is null)
            {
                return;
            }

            if (SetProperty(ref _selectedOutputFormat, value))
            {
                OnPropertyChanged(nameof(SelectedOutputFormatDescription));
                PersistVideoJoinPreferences();
                StatusMessage = $"视频拼接输出格式已切换为 {value.DisplayName}。";
            }
        }
    }

    public string SelectedOutputFormatDescription => SelectedOutputFormat.Description;

    public string OutputDirectory
    {
        get => _outputDirectory;
        set
        {
            var normalizedDirectory = NormalizeOutputDirectory(value);
            if (SetProperty(ref _outputDirectory, normalizedDirectory))
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

    public string OutputDirectoryHintText => "默认使用当前分辨率预设视频所在文件夹；设置后，处理结果会统一输出到所选文件夹。";

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

    public Visibility NonVideoJoinOutputSettingsVisibility =>
        IsVideoJoinModeSelected ? Visibility.Collapsed : Visibility.Visible;

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
                return "添加视频片段后，将默认以首个可用视频作为分辨率预设。";
            }

            var presetTrackItem = GetEffectiveVideoResolutionPresetItem();
            if (presetTrackItem is null)
            {
                return "当前暂无可用的视频片段可作为分辨率预设。";
            }

            return _manualVideoResolutionPresetItem is not null &&
                   ReferenceEquals(_manualVideoResolutionPresetItem, presetTrackItem)
                ? $"当前分辨率预设：{presetTrackItem.SequenceNumberText} · {presetTrackItem.SourceName}"
                : $"当前默认以首个可用视频作为分辨率预设：{presetTrackItem.SequenceNumberText} · {presetTrackItem.SourceName}";
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
            ? $"{removedClipName} 已从视频轨道移除，分辨率预设已切换为 {fallbackPresetTrackItem.SequenceNumberText} 号片段。"
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

        _manualVideoResolutionPresetItem = trackItem;
        RefreshTrackCollection(_videoJoinVideoTrackItems, supportsVideoPreset: true);
        RaiseTrackStatePropertiesChanged();
        StatusMessage = $"{trackItem.SequenceNumberText} 号片段已设为分辨率预设。";
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

            OutputDirectory = selectedFolder;
            StatusMessage = $"已将视频拼接输出目录设置为：{OutputDirectory}";
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

        OutputDirectory = string.Empty;
        StatusMessage = "已清空视频拼接输出目录，处理时将恢复为分辨率预设视频所在文件夹输出。";
    }

    private async Task StartVideoJoinProcessingAsync()
    {
        if (!CanStartVideoJoinProcessing())
        {
            StatusMessage = GetVideoJoinCannotStartMessage();
            return;
        }

        _videoJoinProcessingCancellationSource?.Dispose();
        _videoJoinProcessingCancellationSource = new CancellationTokenSource();
        var cancellationToken = _videoJoinProcessingCancellationSource.Token;

        try
        {
            IsVideoJoinProcessing = true;
            StatusMessage = "正在检查视频拼接素材并整理输出规划...";

            EnsureOutputDirectoryExists();

            var activeTrackItems = _videoJoinVideoTrackItems
                .Where(trackItem => trackItem.IsSourceAvailable)
                .ToArray();
            var presetTrackItem = GetEffectiveVideoResolutionPresetItem() ?? activeTrackItems[0];
            var totalDuration = TimeSpan.Zero;

            foreach (var trackItem in activeTrackItems)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var duration = await ResolveTrackDurationAsync(trackItem.SourcePath, trackItem.DurationText, cancellationToken);
                totalDuration += duration;
            }

            var plannedOutputPath = CreatePlannedVideoJoinOutputPath(presetTrackItem.SourcePath);
            var totalDurationText = totalDuration > TimeSpan.Zero
                ? totalDuration.ToString(@"hh\:mm\:ss")
                : "未知";

            StatusMessage =
                $"已完成 {activeTrackItems.Length} 段视频的拼接校验与输出规划。预设分辨率来源：{presetTrackItem.SequenceNumberText} · {presetTrackItem.ResolutionText}；输出格式：{SelectedOutputFormat.DisplayName}；较小分辨率视频：{GetSmallerResolutionStrategyLabel()}；较大分辨率视频：{GetLargerResolutionStrategyLabel()}；预计输出文件：{Path.GetFileName(plannedOutputPath)}；总时长约：{totalDurationText}。";
        }
        catch (OperationCanceledException) when (_videoJoinProcessingCancellationSource?.IsCancellationRequested == true)
        {
            StatusMessage = "已取消视频拼接任务。";
        }
        catch (Exception exception)
        {
            StatusMessage = "视频拼接任务准备失败，请稍后重试。";
            _logger?.Log(LogLevel.Error, "准备视频拼接任务时发生异常。", exception);
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
        !IsVideoJoinProcessing &&
        _selectedMergeMode == MergeWorkspaceMode.VideoJoin &&
        _videoJoinVideoTrackItems.Any(trackItem => trackItem.IsSourceAvailable);

    private bool CanEditVideoJoinOutputSettings() =>
        !IsVideoJoinProcessing &&
        _selectedMergeMode == MergeWorkspaceMode.VideoJoin;

    private bool CanClearOutputDirectory() =>
        CanEditVideoJoinOutputSettings() &&
        HasCustomOutputDirectory;

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

        return "请先向视频轨道添加至少一个有效视频片段。";
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

    private string CreatePlannedVideoJoinOutputPath(string presetSourcePath) =>
        MediaPathResolver.CreateUniqueOutputPath(
            MediaPathResolver.CreateMergeOutputPath(
                presetSourcePath,
                SelectedOutputFormat.Extension,
                HasCustomOutputDirectory ? OutputDirectory : null));

    private void EnsureOutputDirectoryExists()
    {
        if (HasCustomOutputDirectory)
        {
            Directory.CreateDirectory(OutputDirectory);
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

    private void PersistVideoJoinPreferences()
    {
        if (_userPreferencesService is null)
        {
            return;
        }

        _userPreferencesService.Update(existingPreferences => existingPreferences with
        {
            PreferredMergeVideoJoinOutputFormatExtension = _selectedOutputFormat?.Extension,
            PreferredMergeVideoJoinOutputDirectory = HasCustomOutputDirectory ? OutputDirectory : null,
            PreferredMergeSmallerResolutionStrategy = _selectedSmallerResolutionStrategy,
            PreferredMergeLargerResolutionStrategy = _selectedLargerResolutionStrategy
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

    private static bool TryParseTrackDuration(string durationText, out TimeSpan duration) =>
        TimeSpan.TryParse(durationText, out duration);

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
        OnPropertyChanged(nameof(NonVideoJoinOutputSettingsVisibility));
        OnPropertyChanged(nameof(TimelineHintText));
        OnPropertyChanged(nameof(VideoTrackItems));
        OnPropertyChanged(nameof(AudioTrackItems));
        OnPropertyChanged(nameof(VideoTrackEmptyText));
        OnPropertyChanged(nameof(AudioTrackEmptyText));
        OnPropertyChanged(nameof(VideoJoinTimelineVisibility));
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
        OnPropertyChanged(nameof(VideoJoinTotalDurationText));
        OnPropertyChanged(nameof(AudioTrackSummaryText));
        OnPropertyChanged(nameof(VideoTrackEmptyVisibility));
        OnPropertyChanged(nameof(AudioTrackEmptyVisibility));
        OnPropertyChanged(nameof(VideoResolutionPresetSummaryText));
        OnPropertyChanged(nameof(VideoJoinOutputDirectoryDisplayText));
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
