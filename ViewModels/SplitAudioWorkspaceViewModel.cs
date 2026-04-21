using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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

public sealed partial class SplitAudioWorkspaceViewModel : ObservableObject, IDisposable, ISplitAudioPlaybackParticipant
{
    private readonly ApplicationConfiguration _configuration;
    private readonly ILocalizationService _localizationService;
    private readonly IMediaImportDiscoveryService _mediaImportDiscoveryService;
    private readonly IMediaInfoService _mediaInfoService;
    private readonly IFilePickerService _filePickerService;
    private readonly IFileRevealService _fileRevealService;
    private readonly IDispatcherService _dispatcherService;
    private readonly ILogger _logger;
    private readonly SplitAudioWorkspacePreferencesState _preferencesState;
    private readonly SplitAudioProgressState _progressState;
    private readonly SplitAudioResultCollectionState _resultCollectionState;
    private readonly SplitAudioInputState _inputState;
    private readonly SplitAudioExecutionCoordinator _executionCoordinator;
    private readonly AsyncRelayCommand _selectInputCommand;
    private readonly RelayCommand _removeInputCommand;
    private readonly AsyncRelayCommand _browseOutputDirectoryCommand;
    private readonly RelayCommand _clearOutputDirectoryCommand;
    private readonly AsyncRelayCommand _startSeparationCommand;
    private readonly RelayCommand _cancelSeparationCommand;
    private readonly RelayCommand _revealFileCommand;
    private readonly RelayCommand _clearResultsCommand;

    private string _statusMessage;
    private Func<string>? _statusMessageResolver;
    private bool _isBusy;
    private CancellationTokenSource? _executionCancellationSource;
    private bool _isDisposed;

    internal SplitAudioWorkspaceViewModel(SplitAudioWorkspaceDependencies dependencies)
    {
        ArgumentNullException.ThrowIfNull(dependencies);

        _configuration = dependencies.Configuration;
        _localizationService = dependencies.LocalizationService;
        _mediaImportDiscoveryService = dependencies.MediaImportDiscoveryService;
        _mediaInfoService = dependencies.MediaInfoService;
        _filePickerService = dependencies.FilePickerService;
        _fileRevealService = dependencies.FileRevealService;
        _videoPreviewService = dependencies.VideoPreviewService;
        _dispatcherService = dependencies.DispatcherService;
        _logger = dependencies.Logger;
        _preferencesState = new SplitAudioWorkspacePreferencesState(
            _configuration,
            dependencies.LocalizationService,
            dependencies.UserPreferencesService,
            _logger);
        _progressState = new SplitAudioProgressState(dependencies.LocalizationService);
        _resultCollectionState = new SplitAudioResultCollectionState();
        _inputState = new SplitAudioInputState(GetDefaultInputSummaryText);
        _executionCoordinator = new SplitAudioExecutionCoordinator(
            dependencies.AudioSeparationWorkflowService,
            dependencies.LocalizationService,
            dependencies.UserPreferencesService,
            dependencies.FileRevealService,
            _logger);

        _statusMessage = string.Empty;
        SetStatusMessage(
            "splitAudio.status.ready",
            "请导入 1 个音频或视频文件，系统会先标准化音频，再调用 Demucs 执行四轨拆分。");

        _selectInputCommand = new AsyncRelayCommand(SelectInputAsync, () => !IsBusy);
        _removeInputCommand = new RelayCommand(RemoveInput, () => HasInput && !IsBusy);
        _browseOutputDirectoryCommand = new AsyncRelayCommand(BrowseOutputDirectoryAsync, () => !IsBusy);
        _clearOutputDirectoryCommand = new RelayCommand(ClearOutputDirectory, () => HasCustomOutputDirectory && !IsBusy);
        _startSeparationCommand = new AsyncRelayCommand(StartSeparationAsync, () => HasInput && !IsBusy);
        _cancelSeparationCommand = new RelayCommand(CancelSeparation, () => IsBusy);
        _revealFileCommand = new RelayCommand(RevealFile, CanRevealFile);
        _clearResultsCommand = new RelayCommand(ClearResults, CanClearResults);
        InitializePreview();
    }

    public ObservableCollection<SplitAudioResultItemViewModel> ResultItems => _resultCollectionState.Items;

    public IReadOnlyList<OutputFormatOption> AvailableOutputFormats => _preferencesState.AvailableOutputFormats;

    public IReadOnlyList<DemucsAccelerationModeOption> AvailableAccelerationModes =>
        _preferencesState.AvailableAccelerationModes;

    public ICommand SelectInputCommand => _selectInputCommand;

    public ICommand RemoveInputCommand => _removeInputCommand;

    public ICommand BrowseOutputDirectoryCommand => _browseOutputDirectoryCommand;

    public ICommand ClearOutputDirectoryCommand => _clearOutputDirectoryCommand;

    public ICommand StartSeparationCommand => _startSeparationCommand;

    public ICommand CancelSeparationCommand => _cancelSeparationCommand;

    public ICommand RevealFileCommand => _revealFileCommand;

    public ICommand ClearResultsCommand => _clearResultsCommand;

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public string InputPath => _inputState.InputPath;

    public string InputFileName => _inputState.InputFileName;

    public string InputSummaryText => _inputState.InputSummaryText;

    public bool HasInput => _inputState.HasInput;

    public Visibility PlaceholderVisibility => _inputState.PlaceholderVisibility;

    public Visibility InputCardVisibility => _inputState.InputCardVisibility;

    public Visibility ResultsVisibility => _resultCollectionState.ResultsVisibility;

    public Visibility EmptyResultsVisibility => _resultCollectionState.EmptyResultsVisibility;

    public string InputSectionTitleText =>
        GetLocalizedText("splitAudio.input.sectionTitle", "导入文件");

    public string PlaceholderTitleText =>
        GetLocalizedText("splitAudio.input.placeholderTitle", "请导入文件或拖拽到此处");

    public string ImportButtonText =>
        GetLocalizedText("splitAudio.action.importFile", "导入文件");

    public string RemoveInputButtonText =>
        GetLocalizedText("splitAudio.action.removeInput", "移除");

    public string SettingsSectionTitleText =>
        GetLocalizedText("splitAudio.settings.sectionTitle", "拆音相关设置");

    public string SettingsDescriptionText =>
        GetLocalizedText("splitAudio.settings.description", "输出格式仍由 FFmpeg 负责，Demucs 只负责生成中间四轨。");

    public string OutputFormatLabelText =>
        GetLocalizedText("splitAudio.settings.outputFormatLabel", "输出格式");

    public string OutputDirectoryLabelText =>
        GetLocalizedText("splitAudio.settings.outputDirectoryLabel", "输出目录");

    public string OutputDirectoryPlaceholderText =>
        GetLocalizedText("splitAudio.settings.outputDirectoryPlaceholder", "原文件目录");

    public string SelectOutputDirectoryButtonText =>
        GetLocalizedText("splitAudio.settings.selectDirectory", "选择目录");

    public string ClearOutputDirectoryButtonText =>
        GetLocalizedText("splitAudio.settings.clearDirectory", "清空");

    public string AccelerationModeLabelText =>
        GetLocalizedText("splitAudio.settings.accelerationLabel", "拆音加速模式");

    public string StatusSectionTitleText =>
        GetLocalizedText("splitAudio.settings.statusLabel", "当前状态");

    public string ResultsSectionTitleText =>
        GetLocalizedText("splitAudio.results.sectionTitle", "处理完毕列表");

    public string ResultsDescriptionText =>
        GetLocalizedText("splitAudio.results.description", "每次完成后会按原文件名 + stem 名称输出四个结果文件。");

    public string ClearResultsButtonText =>
        GetLocalizedText("splitAudio.results.clearAll", "全部清空");

    public string EmptyResultsText =>
        GetLocalizedText("splitAudio.results.empty", "暂无处理结果");

    public string RevealFileButtonText =>
        GetLocalizedText("splitAudio.action.revealFile", "定位文件");

    public string PlayPreviewButtonText =>
        GetLocalizedText("splitAudio.preview.play", "播放预览");

    public string PausePreviewButtonText =>
        GetLocalizedText("splitAudio.preview.pause", "暂停预览");

    public string VocalsStemTitleText => GetStemDisplayName(AudioStemKind.Vocals);

    public string DrumsStemTitleText => GetStemDisplayName(AudioStemKind.Drums);

    public string BassStemTitleText => GetStemDisplayName(AudioStemKind.Bass);

    public string OtherStemTitleText => GetStemDisplayName(AudioStemKind.Other);

    public string SupportedInputFormatsHint =>
        FormatLocalizedText(
            "common.workspace.splitAudio.supportedInputFormatsHint",
            "支持导入格式（{formats}）",
            ("formats", string.Join(
                GetLocalizedText("splitAudio.common.listSeparator", "、"),
                _configuration.SupportedSplitAudioInputFileTypes.Select(item => item.TrimStart('.').ToUpperInvariant()))));

    public string DragDropCaptionText =>
        GetLocalizedText("common.workspace.splitAudio.dragDropCaptionText", "导入音频或视频文件");

    public OutputFormatOption SelectedOutputFormat
    {
        get => _preferencesState.SelectedOutputFormat;
        set
        {
            if (value is null)
            {
                return;
            }

            if (_preferencesState.TrySetSelectedOutputFormat(value))
            {
                OnPropertyChanged(nameof(SelectedOutputFormat));
                OnPropertyChanged(nameof(SelectedOutputFormatDescription));
            }
        }
    }

    public string SelectedOutputFormatDescription => SelectedOutputFormat.Description;

    public DemucsAccelerationModeOption SelectedAccelerationMode
    {
        get => _preferencesState.SelectedAccelerationMode;
        set
        {
            if (value is null)
            {
                return;
            }

            if (_preferencesState.TrySetSelectedAccelerationMode(value))
            {
                OnPropertyChanged(nameof(SelectedAccelerationMode));
                OnPropertyChanged(nameof(SelectedAccelerationModeDescription));

                if (!IsBusy)
                {
                    SetStatusMessage(() => _preferencesState.GetAccelerationModeStatusMessage());
                }
            }
        }
    }

    public string SelectedAccelerationModeDescription => SelectedAccelerationMode.Description;

    public string OutputDirectory
    {
        get => _preferencesState.OutputDirectory;
        set
        {
            if (_preferencesState.TrySetOutputDirectory(value))
            {
                OnPropertyChanged(nameof(OutputDirectory));
                OnPropertyChanged(nameof(HasCustomOutputDirectory));
                NotifyCommandStates();
            }
        }
    }

    public bool HasCustomOutputDirectory => _preferencesState.HasCustomOutputDirectory;

    public string OutputDirectoryHintText =>
        GetLocalizedText("splitAudio.settings.outputDirectoryHint", "留空时默认导出到原文件所在文件夹。");

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(CanPlayPreview));
                NotifyCommandStates();
            }
        }
    }

    public Visibility ProgressVisibility => _progressState.ProgressVisibility;

    public bool IsProgressIndeterminate => _progressState.IsProgressIndeterminate;

    public double ProgressValue => _progressState.ProgressValue;

    public string ProgressSummaryText => _progressState.ProgressSummaryText;

    public string ProgressDetailText => _progressState.ProgressDetailText;

    public string ProgressPercentText => _progressState.ProgressPercentText;

    public void RefreshLocalization()
    {
        _preferencesState.ReloadLocalization();
        _inputState.RefreshLocalization();
        _progressState.RefreshLocalization();

        OnPropertyChanged(nameof(AvailableOutputFormats));
        OnPropertyChanged(nameof(SelectedOutputFormat));
        OnPropertyChanged(nameof(SelectedOutputFormatDescription));
        OnPropertyChanged(nameof(AvailableAccelerationModes));
        OnPropertyChanged(nameof(SelectedAccelerationMode));
        OnPropertyChanged(nameof(SelectedAccelerationModeDescription));
        OnPropertyChanged(nameof(InputSectionTitleText));
        OnPropertyChanged(nameof(PlaceholderTitleText));
        OnPropertyChanged(nameof(ImportButtonText));
        OnPropertyChanged(nameof(RemoveInputButtonText));
        OnPropertyChanged(nameof(SettingsSectionTitleText));
        OnPropertyChanged(nameof(SettingsDescriptionText));
        OnPropertyChanged(nameof(OutputFormatLabelText));
        OnPropertyChanged(nameof(OutputDirectoryLabelText));
        OnPropertyChanged(nameof(OutputDirectoryPlaceholderText));
        OnPropertyChanged(nameof(SelectOutputDirectoryButtonText));
        OnPropertyChanged(nameof(ClearOutputDirectoryButtonText));
        OnPropertyChanged(nameof(AccelerationModeLabelText));
        OnPropertyChanged(nameof(StatusSectionTitleText));
        OnPropertyChanged(nameof(ResultsSectionTitleText));
        OnPropertyChanged(nameof(ResultsDescriptionText));
        OnPropertyChanged(nameof(ClearResultsButtonText));
        OnPropertyChanged(nameof(EmptyResultsText));
        OnPropertyChanged(nameof(RevealFileButtonText));
        OnPropertyChanged(nameof(PlayPreviewButtonText));
        OnPropertyChanged(nameof(PausePreviewButtonText));
        OnPropertyChanged(nameof(PlayPauseButtonText));
        OnPropertyChanged(nameof(VocalsStemTitleText));
        OnPropertyChanged(nameof(DrumsStemTitleText));
        OnPropertyChanged(nameof(BassStemTitleText));
        OnPropertyChanged(nameof(OtherStemTitleText));
        OnPropertyChanged(nameof(SupportedInputFormatsHint));
        OnPropertyChanged(nameof(DragDropCaptionText));
        OnPropertyChanged(nameof(OutputDirectoryHintText));
        NotifyInputStateChanged();
        NotifyProgressStateChanged();
        RefreshLocalizedRuntimeText();
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _executionCancellationSource?.Cancel();
        _executionCancellationSource?.Dispose();
        _executionCancellationSource = null;
        DisposePreview();
    }

    public async Task ImportPathsAsync(IEnumerable<string> inputPaths)
    {
        ArgumentNullException.ThrowIfNull(inputPaths);

        if (IsBusy)
        {
            SetStatusMessage(
                "splitAudio.status.busy",
                "当前拆音任务正在执行，请等待完成或先取消。");
            return;
        }

        var normalizedPaths = inputPaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (normalizedPaths.Length == 0)
        {
            return;
        }

        SetStatusMessage(
            "splitAudio.status.organizingImport",
            "正在整理拆音输入...");
        var discovery = await Task.Run(
            () => _mediaImportDiscoveryService.Discover(normalizedPaths, _configuration.SupportedSplitAudioInputFileTypes));

        if (discovery.SupportedFiles.Count == 0)
        {
            if (discovery.UnsupportedEntries > 0 || discovery.MissingEntries > 0)
            {
                SetStatusMessage(
                    "common.workspace.splitAudio.noProcessableImportMessage",
                    "没有发现可用于拆音的音频或视频文件。");
            }
            else
            {
                SetStatusMessage(
                    "splitAudio.status.noNewFiles",
                    "没有新的可处理文件。");
            }

            return;
        }

        string? selectedFilePath = null;
        Func<string>? rejectedMessageResolver = null;

        foreach (var filePath in discovery.SupportedFiles)
        {
            var validationResult = await ValidateInputCandidateAsync(filePath);
            if (validationResult.IsAccepted)
            {
                selectedFilePath = filePath;
                break;
            }

            rejectedMessageResolver = validationResult.ReasonResolver;
        }

        if (string.IsNullOrWhiteSpace(selectedFilePath))
        {
            SetStatusMessage(rejectedMessageResolver ?? (() => GetLocalizedText(
                "splitAudio.status.noAudioTrack",
                "导入文件不包含可用音频轨道，无法拆音。")));
            return;
        }

        await ApplySelectedInputAsync(selectedFilePath);

        if (discovery.SupportedFiles.Count > 1)
        {
            SetStatusMessage(
                () => FormatLocalizedText(
                    "splitAudio.status.importedFirstFromMultiple",
                    "拆音一次仅支持 1 个文件，已导入第一个可处理文件：{fileName}",
                    ("fileName", InputFileName)));
        }
        else
        {
            SetStatusMessage(
                () => FormatLocalizedText(
                    "splitAudio.status.importedReady",
                    "已导入 {fileName}，可以开始拆音。",
                    ("fileName", InputFileName)));
        }
    }

    private async Task SelectInputAsync()
    {
        try
        {
            var selectedFile = await _filePickerService.PickSingleFileAsync(
                new FilePickerRequest(
                    _configuration.SupportedSplitAudioInputFileTypes,
                    GetLocalizedText(
                        "common.workspace.splitAudio.importFilePickerCommitText",
                        "导入音频或视频文件")));

            if (string.IsNullOrWhiteSpace(selectedFile))
            {
                SetStatusMessage(
                    "splitAudio.status.importCancelled",
                    "已取消文件导入。");
                return;
            }

            await ImportPathsAsync(new[] { selectedFile });
        }
        catch (OperationCanceledException)
        {
            SetStatusMessage(
                "splitAudio.status.importCancelled",
                "已取消文件导入。");
        }
        catch (Exception exception)
        {
            SetStatusMessage(
                "splitAudio.status.importFailed",
                "导入文件失败。");
            _logger.Log(LogLevel.Error, "导入拆音文件时发生异常。", exception);
        }
    }

    private void RemoveInput()
    {
        if (!HasInput)
        {
            return;
        }

        ResetPreviewState();
        _ = _videoPreviewService.UnloadAsync();
        OnPropertyChanged(nameof(CanPlayPreview));
        _inputState.Clear();
        NotifyInputStateChanged();
        NotifyCommandStates();
        SetStatusMessage(
            "splitAudio.status.inputRemoved",
            "已清空当前拆音输入。");
    }

    private async Task BrowseOutputDirectoryAsync()
    {
        try
        {
            var selectedDirectory = await _filePickerService.PickFolderAsync(
                GetLocalizedText("splitAudio.picker.outputDirectoryTitle", "选择拆音输出目录"));
            if (string.IsNullOrWhiteSpace(selectedDirectory))
            {
                SetStatusMessage(
                    "splitAudio.status.outputDirectoryCancelled",
                    "已取消选择输出目录。");
                return;
            }

            OutputDirectory = selectedDirectory;
            SetStatusMessage(
                () => FormatLocalizedText(
                    "splitAudio.status.outputDirectoryUpdated",
                    "已将拆音输出目录设置为：{path}",
                    ("path", OutputDirectory)));
        }
        catch (OperationCanceledException)
        {
            SetStatusMessage(
                "splitAudio.status.outputDirectoryCancelled",
                "已取消选择输出目录。");
        }
        catch (Exception exception)
        {
            SetStatusMessage(
                "splitAudio.status.outputDirectoryFailed",
                "选择输出目录失败。");
            _logger.Log(LogLevel.Error, "选择拆音输出目录时发生异常。", exception);
        }
    }

    private void ClearOutputDirectory()
    {
        if (!HasCustomOutputDirectory)
        {
            return;
        }

        OutputDirectory = string.Empty;
        SetStatusMessage(
            "splitAudio.status.outputDirectoryCleared",
            "已清空拆音输出目录，留空时将输出到原文件夹。");
    }

    private async Task StartSeparationAsync()
    {
        if (!HasInput)
        {
            SetStatusMessage(
                "common.workspace.splitAudio.emptyQueueProcessingMessage",
                "请先导入一个可拆音的音频或视频文件。");
            return;
        }

        await PausePreviewForDeactivationAsync();

        _executionCancellationSource?.Dispose();
        _executionCancellationSource = new CancellationTokenSource();

        try
        {
            IsBusy = true;
            ShowPreparationProgress();
            SetStatusMessage(
                "splitAudio.status.processing",
                "正在执行拆音，请稍候...");

            var outcome = await _executionCoordinator.ExecuteAsync(
                InputPath,
                SelectedOutputFormat,
                HasCustomOutputDirectory ? OutputDirectory : null,
                SelectedAccelerationMode.Mode,
                new Progress<AudioSeparationProgress>(UpdateProgress),
                _executionCancellationSource.Token);

            if (outcome.Result is not null)
            {
                _resultCollectionState.Prepend(outcome.Result);
                NotifyResultStateChanged();
            }

            ApplyExecutionOutcomeStatus(outcome);
        }
        finally
        {
            ResetProgress();
            IsBusy = false;
            _executionCancellationSource?.Dispose();
            _executionCancellationSource = null;
        }
    }

    private void CancelSeparation()
    {
        if (!IsBusy)
        {
            return;
        }

        SetStatusMessage(
            "splitAudio.status.cancelling",
            "正在取消当前拆音任务...");
        _executionCancellationSource?.Cancel();
    }

    private void RevealFile(object? parameter)
    {
        if (parameter is not string filePath || string.IsNullOrWhiteSpace(filePath))
        {
            return;
        }

        try
        {
            _fileRevealService.RevealFile(filePath);
        }
        catch (Exception exception)
        {
            _logger.Log(LogLevel.Warning, "定位拆音输出文件失败。", exception);
            SetStatusMessage(
                "splitAudio.status.revealFailed",
                "定位输出文件失败，请检查文件是否仍然存在。");
        }
    }

    private bool CanRevealFile(object? parameter) =>
        parameter is string filePath &&
        !string.IsNullOrWhiteSpace(filePath);

    private void ClearResults()
    {
        if (!_resultCollectionState.HasResults)
        {
            return;
        }

        var clearedCount = ResultItems.Count;
        _resultCollectionState.Clear();
        NotifyResultStateChanged();
        SetStatusMessage(
            () => FormatLocalizedText(
                "splitAudio.status.resultsCleared",
                "已清空处理完毕列表，共移除 {count} 条记录。",
                ("count", clearedCount)));
    }

    private bool CanClearResults() => _resultCollectionState.HasResults;

    private void UpdateProgress(AudioSeparationProgress progress)
    {
        if (_dispatcherService.HasThreadAccess)
        {
            ApplyProgress(progress);
            return;
        }

        _dispatcherService.TryEnqueue(() => ApplyProgress(progress));
    }

    private void ApplyProgress(AudioSeparationProgress progress)
    {
        _progressState.Apply(progress);
        NotifyProgressStateChanged();
    }

    private void ShowPreparationProgress()
    {
        _progressState.ShowPreparation();
        NotifyProgressStateChanged();
    }

    private void ResetProgress()
    {
        if (_dispatcherService.HasThreadAccess)
        {
            HideProgressCore();
            return;
        }

        _dispatcherService.TryEnqueue(HideProgressCore);
    }

    private void HideProgressCore()
    {
        _progressState.Reset();
        NotifyProgressStateChanged();
    }

    private async Task<InputValidationResult> ValidateInputCandidateAsync(string inputPath)
    {
        try
        {
            if (_mediaInfoService.TryGetCachedDetails(inputPath, out var cachedSnapshot) &&
                !cachedSnapshot.HasAudioStream)
            {
                return InputValidationResult.Rejected(() => CreateNoAudioTrackMessage(Path.GetFileName(inputPath)));
            }

            var detailsResult = await _mediaInfoService.GetMediaDetailsAsync(inputPath);
            if (detailsResult.IsSuccess && detailsResult.Snapshot is { HasAudioStream: false })
            {
                return InputValidationResult.Rejected(() => CreateNoAudioTrackMessage(Path.GetFileName(inputPath)));
            }

            return InputValidationResult.Accepted();
        }
        catch (Exception exception)
        {
            _logger.Log(LogLevel.Warning, $"提前校验拆音文件时发生异常，将在执行时继续校验：{Path.GetFileName(inputPath)}", exception);
            return InputValidationResult.Accepted();
        }
    }

    private async Task ApplySelectedInputAsync(string inputPath)
    {
        var normalizedPath = Path.GetFullPath(inputPath);
        ResetPreviewState();
        var inputSummaryResolver = await BuildInputSummaryResolverAsync(normalizedPath);
        _inputState.SetSelectedInput(normalizedPath, inputSummaryResolver);
        await PrimePreviewTimelineAsync(normalizedPath);
        OnPropertyChanged(nameof(CanPlayPreview));
        NotifyInputStateChanged();
        NotifyCommandStates();
    }

    private async Task<Func<string>> BuildInputSummaryResolverAsync(string inputPath)
    {
        try
        {
            MediaDetailsSnapshot? snapshot = null;

            if (_mediaInfoService.TryGetCachedDetails(inputPath, out var cachedSnapshot))
            {
                snapshot = cachedSnapshot;
            }
            else
            {
                var detailsResult = await _mediaInfoService.GetMediaDetailsAsync(inputPath);
                if (detailsResult.IsSuccess)
                {
                    snapshot = detailsResult.Snapshot;
                }
            }

            if (snapshot is null)
            {
                return () => GetLocalizedText(
                    "splitAudio.input.summary.pendingValidation",
                    "已导入文件；开始拆音时会再次校验音轨并标准化为 WAV。");
            }

            var isVideoInput = snapshot.HasVideoStream;
            var mediaDuration = snapshot.MediaDuration;
            var primaryAudioCodecName = snapshot.PrimaryAudioCodecName;

            return () =>
            {
                var mediaType = GetLocalizedText(
                    isVideoInput
                        ? "splitAudio.input.summary.mediaType.video"
                        : "splitAudio.input.summary.mediaType.audio",
                    isVideoInput ? "视频输入" : "音频输入");
                var durationText = mediaDuration is { } duration && duration > TimeSpan.Zero
                    ? FormatCompactDuration(duration)
                    : GetLocalizedText("splitAudio.input.summary.unknownDuration", "时长未知");
                var audioCodecText = string.IsNullOrWhiteSpace(primaryAudioCodecName)
                    ? GetLocalizedText("splitAudio.input.summary.unknownCodec", "音频编码未知")
                    : FormatLocalizedText(
                        "splitAudio.input.summary.codec",
                        "音频编码 {codec}",
                        ("codec", primaryAudioCodecName));

                return FormatLocalizedText(
                    "splitAudio.input.summary.ready",
                    "{mediaType}，{duration}，{audioCodec}。拆音时会先标准化为临时 WAV，再调用 Demucs 分离四轨。",
                    ("mediaType", mediaType),
                    ("duration", durationText),
                    ("audioCodec", audioCodecText));
            };
        }
        catch (Exception exception)
        {
            _logger.Log(LogLevel.Warning, "读取拆音输入摘要失败，将继续使用简化说明。", exception);
            return () => GetLocalizedText(
                "splitAudio.input.summary.pendingValidation",
                "已导入文件；开始拆音时会再次校验音轨并标准化为 WAV。");
        }
    }

    private void ApplyExecutionOutcomeStatus(SplitAudioExecutionOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);

        switch (outcome.Kind)
        {
            case SplitAudioExecutionOutcomeKind.Succeeded when outcome.Result is not null:
                SetStatusMessage(
                    () => FormatLocalizedText(
                        "splitAudio.status.completed",
                        "{resolutionSummary} 已生成 {count} 条分轨文件。",
                        ("resolutionSummary", outcome.Result.ExecutionPlan.ResolveResolutionSummary()),
                        ("count", outcome.Result.StemOutputs.Count)));
                return;

            case SplitAudioExecutionOutcomeKind.Cancelled:
                SetStatusMessage(
                    "splitAudio.status.cancelled",
                    "拆音任务已取消。");
                return;

            default:
                var failureReasonResolver = outcome.FailureReasonResolver ??
                                            (() => GetLocalizedText("splitAudio.status.failedGenericReason", "未知错误"));
                SetStatusMessage(
                    () => FormatLocalizedText(
                        "splitAudio.status.failed",
                        "拆音失败：{reason}",
                        ("reason", failureReasonResolver())));
                return;
        }
    }

    private void RefreshLocalizedRuntimeText()
    {
        if (_statusMessageResolver is not null)
        {
            StatusMessage = _statusMessageResolver();
        }
    }

    private void SetStatusMessage(Func<string> resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);

        _statusMessageResolver = resolver;
        StatusMessage = resolver();
    }

    private void SetStatusMessage(string key, string fallback) =>
        SetStatusMessage(() => GetLocalizedText(key, fallback));

    private void NotifyCommandStates()
    {
        _selectInputCommand.NotifyCanExecuteChanged();
        _removeInputCommand.NotifyCanExecuteChanged();
        _browseOutputDirectoryCommand.NotifyCanExecuteChanged();
        _clearOutputDirectoryCommand.NotifyCanExecuteChanged();
        _startSeparationCommand.NotifyCanExecuteChanged();
        _cancelSeparationCommand.NotifyCanExecuteChanged();
        _revealFileCommand.NotifyCanExecuteChanged();
    }

    private void NotifyProgressStateChanged()
    {
        OnPropertyChanged(nameof(ProgressVisibility));
        OnPropertyChanged(nameof(IsProgressIndeterminate));
        OnPropertyChanged(nameof(ProgressValue));
        OnPropertyChanged(nameof(ProgressSummaryText));
        OnPropertyChanged(nameof(ProgressDetailText));
        OnPropertyChanged(nameof(ProgressPercentText));
    }

    private void NotifyInputStateChanged()
    {
        OnPropertyChanged(nameof(InputPath));
        OnPropertyChanged(nameof(InputFileName));
        OnPropertyChanged(nameof(InputSummaryText));
        OnPropertyChanged(nameof(HasInput));
        OnPropertyChanged(nameof(PlaceholderVisibility));
        OnPropertyChanged(nameof(InputCardVisibility));
    }

    private void NotifyResultStateChanged()
    {
        OnPropertyChanged(nameof(ResultsVisibility));
        OnPropertyChanged(nameof(EmptyResultsVisibility));
        _clearResultsCommand.NotifyCanExecuteChanged();
    }

    private string GetDefaultInputSummaryText() =>
        GetLocalizedText(
            "splitAudio.input.summary.default",
            "支持视频与纯音频输入；如果导入视频，会自动提取主音轨后再开始拆音。");

    private string CreateNoAudioTrackMessage(string? fileName) =>
        FormatLocalizedText(
            "splitAudio.validation.noAudioTrack",
            "{fileName} 不包含可用音频轨道。",
            ("fileName", string.IsNullOrWhiteSpace(fileName)
                ? GetLocalizedText("splitAudio.input.summary.unknownFileName", "当前文件")
                : fileName));

    private string GetStemDisplayName(AudioStemKind stemKind) =>
        stemKind switch
        {
            AudioStemKind.Vocals => GetLocalizedText("splitAudio.stem.vocals", "人声"),
            AudioStemKind.Drums => GetLocalizedText("splitAudio.stem.drums", "鼓组"),
            AudioStemKind.Bass => GetLocalizedText("splitAudio.stem.bass", "低频"),
            AudioStemKind.Other => GetLocalizedText("splitAudio.stem.other", "其他"),
            _ => throw new ArgumentOutOfRangeException(nameof(stemKind), stemKind, null)
        };

    private string GetLocalizedText(string key, string fallback) =>
        _localizationService.GetString(key, fallback);

    private string FormatLocalizedText(
        string key,
        string fallback,
        params (string Name, object? Value)[] arguments)
    {
        if (arguments.Length == 0)
        {
            return GetLocalizedText(key, fallback);
        }

        var localizedArguments = arguments.ToDictionary(
            argument => argument.Name,
            argument => argument.Value,
            StringComparer.Ordinal);
        return _localizationService.Format(key, localizedArguments, fallback);
    }

    private static string FormatCompactDuration(TimeSpan duration) =>
        duration.TotalHours >= 1d
            ? duration.ToString(@"hh\:mm\:ss")
            : duration.ToString(@"mm\:ss");

    private readonly record struct InputValidationResult(bool IsAccepted, Func<string>? ReasonResolver)
    {
        public static InputValidationResult Accepted() => new(true, null);

        public static InputValidationResult Rejected(Func<string> reasonResolver) => new(false, reasonResolver);
    }
}
