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
    private readonly IMediaImportDiscoveryService _mediaImportDiscoveryService;
    private readonly IMediaInfoService _mediaInfoService;
    private readonly IAudioSeparationWorkflowService _audioSeparationWorkflowService;
    private readonly IFilePickerService _filePickerService;
    private readonly IUserPreferencesService _userPreferencesService;
    private readonly IFileRevealService _fileRevealService;
    private readonly IDispatcherService _dispatcherService;
    private readonly ILogger _logger;
    private readonly AsyncRelayCommand _selectInputCommand;
    private readonly RelayCommand _removeInputCommand;
    private readonly AsyncRelayCommand _browseOutputDirectoryCommand;
    private readonly RelayCommand _clearOutputDirectoryCommand;
    private readonly AsyncRelayCommand _startSeparationCommand;
    private readonly RelayCommand _cancelSeparationCommand;
    private readonly RelayCommand _revealFileCommand;

    private OutputFormatOption? _selectedOutputFormat;
    private DemucsAccelerationModeOption? _selectedAccelerationMode;
    private string _outputDirectory = string.Empty;
    private string _statusMessage;
    private string _inputPath = string.Empty;
    private string _inputFileName = string.Empty;
    private string _inputSummaryText;
    private bool _isBusy;
    private CancellationTokenSource? _executionCancellationSource;
    private Visibility _progressVisibility = Visibility.Collapsed;
    private bool _isProgressIndeterminate;
    private double _progressValue;
    private string _progressSummaryText = string.Empty;
    private string _progressDetailText = string.Empty;
    private string _progressPercentText = string.Empty;
    private bool _isDisposed;

    internal SplitAudioWorkspaceViewModel(SplitAudioWorkspaceDependencies dependencies)
    {
        ArgumentNullException.ThrowIfNull(dependencies);

        _configuration = dependencies.Configuration;
        _mediaImportDiscoveryService = dependencies.MediaImportDiscoveryService;
        _mediaInfoService = dependencies.MediaInfoService;
        _audioSeparationWorkflowService = dependencies.AudioSeparationWorkflowService;
        _filePickerService = dependencies.FilePickerService;
        _userPreferencesService = dependencies.UserPreferencesService;
        _fileRevealService = dependencies.FileRevealService;
        _videoPreviewService = dependencies.VideoPreviewService;
        _dispatcherService = dependencies.DispatcherService;
        _logger = dependencies.Logger;

        var preferences = _userPreferencesService.Load();
        _selectedOutputFormat = ResolvePreferredOutputFormat(preferences.PreferredSplitAudioOutputFormatExtension);
        _selectedAccelerationMode = ResolvePreferredAccelerationMode(preferences.PreferredSplitAudioAccelerationMode);
        _outputDirectory = NormalizeOutputDirectory(preferences.PreferredSplitAudioOutputDirectory);
        _statusMessage = "请导入 1 个音频或视频文件，系统会先标准化音频，再调用 Demucs 执行四轨拆分。";
        _inputSummaryText = "支持视频与纯音频输入；如果导入视频，会自动提取主音轨后再开始拆音。";

        _selectInputCommand = new AsyncRelayCommand(SelectInputAsync, () => !IsBusy);
        _removeInputCommand = new RelayCommand(RemoveInput, () => HasInput && !IsBusy);
        _browseOutputDirectoryCommand = new AsyncRelayCommand(BrowseOutputDirectoryAsync, () => !IsBusy);
        _clearOutputDirectoryCommand = new RelayCommand(ClearOutputDirectory, () => HasCustomOutputDirectory && !IsBusy);
        _startSeparationCommand = new AsyncRelayCommand(StartSeparationAsync, () => HasInput && !IsBusy);
        _cancelSeparationCommand = new RelayCommand(CancelSeparation, () => IsBusy);
        _revealFileCommand = new RelayCommand(RevealFile, CanRevealFile);
        InitializePreview();
    }

    public ObservableCollection<SplitAudioResultItemViewModel> ResultItems { get; } = new();

    public IReadOnlyList<OutputFormatOption> AvailableOutputFormats => _configuration.SupportedAudioOutputFormats;

    public IReadOnlyList<DemucsAccelerationModeOption> AvailableAccelerationModes =>
        _configuration.SupportedSplitAudioAccelerationModes;

    public ICommand SelectInputCommand => _selectInputCommand;

    public ICommand RemoveInputCommand => _removeInputCommand;

    public ICommand BrowseOutputDirectoryCommand => _browseOutputDirectoryCommand;

    public ICommand ClearOutputDirectoryCommand => _clearOutputDirectoryCommand;

    public ICommand StartSeparationCommand => _startSeparationCommand;

    public ICommand CancelSeparationCommand => _cancelSeparationCommand;

    public ICommand RevealFileCommand => _revealFileCommand;

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public string InputPath
    {
        get => _inputPath;
        private set => SetProperty(ref _inputPath, value);
    }

    public string InputFileName
    {
        get => _inputFileName;
        private set => SetProperty(ref _inputFileName, value);
    }

    public string InputSummaryText
    {
        get => _inputSummaryText;
        private set => SetProperty(ref _inputSummaryText, value);
    }

    public bool HasInput => !string.IsNullOrWhiteSpace(InputPath);

    public Visibility PlaceholderVisibility => HasInput ? Visibility.Collapsed : Visibility.Visible;

    public Visibility InputCardVisibility => HasInput ? Visibility.Visible : Visibility.Collapsed;

    public Visibility ResultsVisibility => ResultItems.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

    public Visibility EmptyResultsVisibility => ResultItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    public string SupportedInputFormatsHint =>
        "支持导入格式（" +
        string.Join("、", _configuration.SupportedSplitAudioInputFileTypes.Select(item => item.TrimStart('.').ToUpperInvariant())) +
        "）";

    public string DragDropCaptionText => "导入音频或视频文件";

    public OutputFormatOption SelectedOutputFormat
    {
        get => _selectedOutputFormat ?? AvailableOutputFormats.First();
        set
        {
            if (value is null)
            {
                return;
            }

            if (SetProperty(ref _selectedOutputFormat, value))
            {
                OnPropertyChanged(nameof(SelectedOutputFormatDescription));
                PersistPreferences();
            }
        }
    }

    public string SelectedOutputFormatDescription => SelectedOutputFormat.Description;

    public DemucsAccelerationModeOption SelectedAccelerationMode
    {
        get => _selectedAccelerationMode ?? AvailableAccelerationModes.First();
        set
        {
            if (value is null)
            {
                return;
            }

            if (SetProperty(ref _selectedAccelerationMode, value))
            {
                OnPropertyChanged(nameof(SelectedAccelerationModeDescription));
                PersistPreferences();

                if (!IsBusy)
                {
                    StatusMessage = value.Mode == DemucsAccelerationMode.GpuPreferred
                        ? "已切换为 GPU 优先模式，将按独显 -> 核显 -> CPU 的顺序尝试拆音。"
                        : "已切换为 CPU 兼容模式，本次拆音将固定使用 CPU。";
                }
            }
        }
    }

    public string SelectedAccelerationModeDescription => SelectedAccelerationMode.Description;

    public string OutputDirectory
    {
        get => _outputDirectory;
        set
        {
            var normalized = NormalizeOutputDirectory(value);
            if (SetProperty(ref _outputDirectory, normalized))
            {
                OnPropertyChanged(nameof(HasCustomOutputDirectory));
                PersistPreferences();
                NotifyCommandStates();
            }
        }
    }

    public bool HasCustomOutputDirectory => !string.IsNullOrWhiteSpace(OutputDirectory);

    public string OutputDirectoryHintText => "留空时默认导出到原文件所在文件夹。";

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

    public Visibility ProgressVisibility
    {
        get => _progressVisibility;
        private set => SetProperty(ref _progressVisibility, value);
    }

    public bool IsProgressIndeterminate
    {
        get => _isProgressIndeterminate;
        private set => SetProperty(ref _isProgressIndeterminate, value);
    }

    public double ProgressValue
    {
        get => _progressValue;
        private set => SetProperty(ref _progressValue, value);
    }

    public string ProgressSummaryText
    {
        get => _progressSummaryText;
        private set => SetProperty(ref _progressSummaryText, value);
    }

    public string ProgressDetailText
    {
        get => _progressDetailText;
        private set => SetProperty(ref _progressDetailText, value);
    }

    public string ProgressPercentText
    {
        get => _progressPercentText;
        private set => SetProperty(ref _progressPercentText, value);
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
            StatusMessage = "当前拆音任务正在执行，请等待完成或先取消。";
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

        StatusMessage = "正在整理拆音输入...";
        var discovery = await Task.Run(
            () => _mediaImportDiscoveryService.Discover(normalizedPaths, _configuration.SupportedSplitAudioInputFileTypes));

        if (discovery.SupportedFiles.Count == 0)
        {
            StatusMessage = discovery.UnsupportedEntries > 0 || discovery.MissingEntries > 0
                ? "未发现可用于拆音的音频或视频文件。"
                : "没有新的可处理文件。";
            return;
        }

        string? selectedFilePath = null;
        string? rejectedMessage = null;

        foreach (var filePath in discovery.SupportedFiles)
        {
            var validationResult = await ValidateInputCandidateAsync(filePath);
            if (validationResult.IsAccepted)
            {
                selectedFilePath = filePath;
                break;
            }

            rejectedMessage = validationResult.Reason;
        }

        if (string.IsNullOrWhiteSpace(selectedFilePath))
        {
            StatusMessage = rejectedMessage ?? "导入文件不包含可用音频轨道，无法拆音。";
            return;
        }

        await ApplySelectedInputAsync(selectedFilePath);

        if (discovery.SupportedFiles.Count > 1)
        {
            StatusMessage = $"拆音一次仅支持 1 个文件，已导入第一个可处理文件：{InputFileName}";
        }
        else
        {
            StatusMessage = $"已导入 {InputFileName}，可以开始拆音。";
        }
    }

    private async Task SelectInputAsync()
    {
        try
        {
            var selectedFile = await _filePickerService.PickSingleFileAsync(
                new FilePickerRequest(_configuration.SupportedSplitAudioInputFileTypes, "导入拆音文件"));

            if (string.IsNullOrWhiteSpace(selectedFile))
            {
                StatusMessage = "已取消文件导入。";
                return;
            }

            await ImportPathsAsync(new[] { selectedFile });
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "已取消文件导入。";
        }
        catch (Exception exception)
        {
            StatusMessage = "导入文件失败。";
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
        InputPath = string.Empty;
        InputFileName = string.Empty;
        InputSummaryText = "支持视频与纯音频输入；如果导入视频，会自动提取主音轨后再开始拆音。";
        OnPropertyChanged(nameof(HasInput));
        OnPropertyChanged(nameof(PlaceholderVisibility));
        OnPropertyChanged(nameof(InputCardVisibility));
        NotifyCommandStates();
        StatusMessage = "已清空当前拆音输入。";
    }

    private async Task BrowseOutputDirectoryAsync()
    {
        try
        {
            var selectedDirectory = await _filePickerService.PickFolderAsync("选择拆音输出目录");
            if (string.IsNullOrWhiteSpace(selectedDirectory))
            {
                StatusMessage = "已取消选择输出目录。";
                return;
            }

            OutputDirectory = selectedDirectory;
            StatusMessage = $"已将拆音输出目录设置为：{OutputDirectory}";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "已取消选择输出目录。";
        }
        catch (Exception exception)
        {
            StatusMessage = "选择输出目录失败。";
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
        StatusMessage = "已清空拆音输出目录，留空时将输出到原文件夹。";
    }

    private async Task StartSeparationAsync()
    {
        if (!HasInput)
        {
            StatusMessage = "请先导入一个可拆音文件。";
            return;
        }

        await PausePreviewForDeactivationAsync();

        _executionCancellationSource?.Dispose();
        _executionCancellationSource = new CancellationTokenSource();

        try
        {
            IsBusy = true;
            ShowPreparationProgress();
            StatusMessage = "正在执行拆音，请稍候...";

            var progressReporter = new Progress<AudioSeparationProgress>(UpdateProgress);
            var result = await _audioSeparationWorkflowService.SeparateAsync(
                new AudioSeparationRequest(
                    InputPath,
                    SelectedOutputFormat,
                    HasCustomOutputDirectory ? OutputDirectory : null,
                    progressReporter,
                    SelectedAccelerationMode.Mode),
                _executionCancellationSource.Token);

            ResultItems.Insert(0, new SplitAudioResultItemViewModel(result));
            OnPropertyChanged(nameof(ResultsVisibility));
            OnPropertyChanged(nameof(EmptyResultsVisibility));

            StatusMessage = $"{result.ExecutionPlan.ResolutionSummary} 已生成 4 条 {SelectedOutputFormat.DisplayName} 分轨文件。";
            TryRevealOutput(result);
        }
        catch (OperationCanceledException) when (_executionCancellationSource?.IsCancellationRequested == true)
        {
            StatusMessage = "拆音任务已取消。";
        }
        catch (Exception exception)
        {
            StatusMessage = $"拆音失败：{exception.Message}";
            _logger.Log(LogLevel.Error, "执行拆音任务时发生异常。", exception);
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

        StatusMessage = "正在取消当前拆音任务...";
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
            StatusMessage = "定位输出文件失败，请检查文件是否仍然存在。";
        }
    }

    private bool CanRevealFile(object? parameter) =>
        parameter is string filePath &&
        !string.IsNullOrWhiteSpace(filePath);

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
        ProgressVisibility = Visibility.Visible;
        ProgressSummaryText = progress.StageTitle;
        ProgressDetailText = progress.DetailText;

        if (progress.ProgressRatio is double ratio)
        {
            var normalized = Math.Clamp(ratio, 0d, 1d);
            IsProgressIndeterminate = false;
            ProgressValue = Math.Round(normalized * 100d, 1);
            ProgressPercentText = $"{Math.Round(normalized * 100d):0}%";
            return;
        }

        IsProgressIndeterminate = true;
        ProgressPercentText = "处理中";
    }

    private void ShowPreparationProgress()
    {
        ProgressVisibility = Visibility.Visible;
        IsProgressIndeterminate = true;
        ProgressValue = 0d;
        ProgressSummaryText = "准备开始";
        ProgressDetailText = "正在校验输入并准备拆音运行环境...";
        ProgressPercentText = "准备中";
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
        ProgressVisibility = Visibility.Collapsed;
        IsProgressIndeterminate = false;
        ProgressValue = 0d;
        ProgressSummaryText = string.Empty;
        ProgressDetailText = string.Empty;
        ProgressPercentText = string.Empty;
    }

    private async Task<InputValidationResult> ValidateInputCandidateAsync(string inputPath)
    {
        try
        {
            if (_mediaInfoService.TryGetCachedDetails(inputPath, out var cachedSnapshot) &&
                !cachedSnapshot.HasAudioStream)
            {
                return InputValidationResult.Rejected($"{Path.GetFileName(inputPath)} 不包含可用音频轨道。");
            }

            var detailsResult = await _mediaInfoService.GetMediaDetailsAsync(inputPath);
            if (detailsResult.IsSuccess && detailsResult.Snapshot is { HasAudioStream: false })
            {
                return InputValidationResult.Rejected($"{Path.GetFileName(inputPath)} 不包含可用音频轨道。");
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
        InputPath = normalizedPath;
        InputFileName = Path.GetFileName(normalizedPath);
        InputSummaryText = await BuildInputSummaryAsync(normalizedPath);
        await PrimePreviewTimelineAsync(normalizedPath);
        OnPropertyChanged(nameof(CanPlayPreview));
        OnPropertyChanged(nameof(HasInput));
        OnPropertyChanged(nameof(PlaceholderVisibility));
        OnPropertyChanged(nameof(InputCardVisibility));
        NotifyCommandStates();
    }

    private async Task<string> BuildInputSummaryAsync(string inputPath)
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
                return "已导入文件；开始拆音时会再次校验音轨并标准化为 WAV。";
            }

            var mediaType = snapshot.HasVideoStream ? "视频输入" : "音频输入";
            var durationText = snapshot.MediaDuration is { } duration && duration > TimeSpan.Zero
                ? (duration.TotalHours >= 1d ? duration.ToString(@"hh\:mm\:ss") : duration.ToString(@"mm\:ss"))
                : "时长未知";
            var audioCodecText = string.IsNullOrWhiteSpace(snapshot.PrimaryAudioCodecName)
                ? "音频编码未知"
                : $"音频编码 {snapshot.PrimaryAudioCodecName}";

            return $"{mediaType}，{durationText}，{audioCodecText}。拆音时会先标准化为临时 WAV，再调用 Demucs 分离四轨。";
        }
        catch (Exception exception)
        {
            _logger.Log(LogLevel.Warning, "读取拆音输入摘要失败，将继续使用简化说明。", exception);
            return "已导入文件；开始拆音时会再次校验音轨并标准化为 WAV。";
        }
    }

    private void TryRevealOutput(AudioSeparationResult result)
    {
        try
        {
            if (!_userPreferencesService.Load().RevealOutputFileAfterProcessing)
            {
                return;
            }

            var preferredOutput = result.StemOutputs.FirstOrDefault()?.FilePath;
            if (!string.IsNullOrWhiteSpace(preferredOutput))
            {
                _fileRevealService.RevealFile(preferredOutput);
            }
        }
        catch (Exception exception)
        {
            _logger.Log(LogLevel.Warning, "拆音完成后定位输出文件失败。", exception);
        }
    }

    private void PersistPreferences()
    {
        _userPreferencesService.Update(existingPreferences => existingPreferences with
        {
            PreferredSplitAudioOutputFormatExtension = SelectedOutputFormat.Extension,
            PreferredSplitAudioOutputDirectory = HasCustomOutputDirectory ? OutputDirectory : null,
            PreferredSplitAudioAccelerationMode = SelectedAccelerationMode.Mode
        });
    }

    private OutputFormatOption ResolvePreferredOutputFormat(string? preferredExtension)
    {
        if (!string.IsNullOrWhiteSpace(preferredExtension))
        {
            var preferredFormat = AvailableOutputFormats.FirstOrDefault(format =>
                string.Equals(format.Extension, preferredExtension, StringComparison.OrdinalIgnoreCase));
            if (preferredFormat is not null)
            {
                return preferredFormat;
            }
        }

        return AvailableOutputFormats.First();
    }

    private DemucsAccelerationModeOption ResolvePreferredAccelerationMode(DemucsAccelerationMode preferredMode)
    {
        var preferredOption = AvailableAccelerationModes.FirstOrDefault(option => option.Mode == preferredMode);
        return preferredOption ?? AvailableAccelerationModes.First();
    }

    private string NormalizeOutputDirectory(string? outputDirectory)
    {
        if (MediaPathResolver.TryNormalizeOutputDirectory(outputDirectory, out var normalizedDirectory))
        {
            return normalizedDirectory;
        }

        _logger.Log(LogLevel.Warning, "检测到无效的拆音输出目录配置，已回退为原文件夹输出。");
        return string.Empty;
    }

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

    private readonly record struct InputValidationResult(bool IsAccepted, string? Reason)
    {
        public static InputValidationResult Accepted() => new(true, null);

        public static InputValidationResult Rejected(string reason) => new(false, reason);
    }
}
