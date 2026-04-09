using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
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

public sealed class MainViewModel : ObservableObject, IDisposable
{
    private const string RuntimePreparingMessage = "正在准备运行环境...";
    private const string ReadyForImportMessage = "请导入视频文件或文件夹。";
    private const string ReadyForProcessingMessage = "文件已准备完成，可以开始处理。";
    private const string RuntimePreparationCancelledMessage = "运行环境准备已取消。";
    private const string RuntimePreparationFailedMessage = "运行环境准备失败，请检查网络或运行目录。";

    private static readonly IReadOnlyList<ThemePreferenceOption> ThemePreferenceOptions =
        new[]
        {
            new ThemePreferenceOption(ThemePreference.UseSystem, "跟随系统", "根据 Windows 当前主题自动切换明亮和暗黑外观。"),
            new ThemePreferenceOption(ThemePreference.Light, "明亮主题", "始终使用明亮外观，适合高亮环境。"),
            new ThemePreferenceOption(ThemePreference.Dark, "暗黑主题", "始终使用暗黑外观，适合低亮环境。")
        };

    private readonly ApplicationConfiguration _configuration;
    private readonly IFFmpegRuntimeService _ffmpegRuntimeService;
    private readonly IFFmpegService _ffmpegService;
    private readonly IMediaInfoService _mediaInfoService;
    private readonly IFFmpegCommandBuilder _ffmpegCommandBuilder;
    private readonly IMediaImportDiscoveryService _mediaImportDiscoveryService;
    private readonly ILogger _logger;
    private readonly IFilePickerService _filePickerService;
    private readonly IDispatcherService _dispatcherService;
    private readonly IUserPreferencesService _userPreferencesService;
    private readonly IFileRevealService _fileRevealService;
    private readonly AsyncRelayCommand _selectFilesCommand;
    private readonly AsyncRelayCommand _selectFolderCommand;
    private readonly AsyncRelayCommand _selectOutputDirectoryCommand;
    private readonly AsyncRelayCommand _executeProcessingCommand;
    private readonly RelayCommand _clearQueueCommand;
    private readonly RelayCommand _clearOutputDirectoryCommand;
    private readonly RelayCommand _removeImportItemCommand;
    private readonly RelayCommand _cancelExecutionCommand;
    private readonly RelayCommand _toggleSettingsPaneCommand;
    private readonly RelayCommand _closeSettingsPaneCommand;
    private readonly RelayCommand _showMediaDetailsCommand;
    private readonly RelayCommand _closeMediaDetailsCommand;

    private string? _runtimeExecutablePath;
    private string _statusMessage;
    private bool _isBusy;
    private CancellationTokenSource? _executionCancellationSource;
    private CancellationTokenSource? _detailLoadCancellationSource;
    private int _detailLoadVersion;
    private MediaJobViewModel? _pendingDetailItem;
    private ProcessingModeOption? _selectedProcessingMode;
    private OutputFormatOption? _selectedOutputFormat;
    private string _outputDirectory = string.Empty;
    private ThemePreferenceOption? _selectedThemeOption;
    private bool _revealOutputFileAfterProcessing;
    private bool _isSettingsPaneOpen;
    private bool _isDisposed;

    public MainViewModel(
        ApplicationConfiguration configuration,
        IFFmpegRuntimeService ffmpegRuntimeService,
        IFFmpegService ffmpegService,
        IMediaInfoService mediaInfoService,
        IFFmpegCommandBuilder ffmpegCommandBuilder,
        IMediaImportDiscoveryService mediaImportDiscoveryService,
        ILogger logger,
        IFilePickerService filePickerService,
        IDispatcherService dispatcherService,
        IUserPreferencesService userPreferencesService,
        IFileRevealService fileRevealService)
    {
        _configuration = configuration;
        _ffmpegRuntimeService = ffmpegRuntimeService;
        _ffmpegService = ffmpegService;
        _mediaInfoService = mediaInfoService;
        _ffmpegCommandBuilder = ffmpegCommandBuilder;
        _mediaImportDiscoveryService = mediaImportDiscoveryService;
        _logger = logger;
        _filePickerService = filePickerService;
        _dispatcherService = dispatcherService;
        _userPreferencesService = userPreferencesService;
        _fileRevealService = fileRevealService;
        _statusMessage = RuntimePreparingMessage;

        ThemeOptions = ThemePreferenceOptions;

        LogEntries = new ObservableCollection<LogEntry>();
        ImportItems = new ObservableCollection<MediaJobViewModel>();
        AvailableOutputFormats = new ObservableCollection<OutputFormatOption>();
        DetailPanel = new MediaDetailPanelViewModel();
        ProcessingModes = _configuration.SupportedProcessingModes;

        var userPreferences = _userPreferencesService.Load();
        _selectedThemeOption = ThemeOptions.FirstOrDefault(option => option.Preference == userPreferences.ThemePreference) ?? ThemeOptions[0];
        _outputDirectory = NormalizeOutputDirectory(userPreferences.PreferredOutputDirectory);
        _revealOutputFileAfterProcessing = userPreferences.RevealOutputFileAfterProcessing;

        ImportItems.CollectionChanged += OnImportItemsChanged;

        _selectFilesCommand = new AsyncRelayCommand(SelectFilesAsync, () => CanModifyInputs);
        _selectFolderCommand = new AsyncRelayCommand(SelectFolderAsync, () => CanModifyInputs);
        _selectOutputDirectoryCommand = new AsyncRelayCommand(SelectOutputDirectoryAsync, () => CanModifyInputs);
        _executeProcessingCommand = new AsyncRelayCommand(ExecuteProcessingAsync, CanExecuteProcessing);
        _clearQueueCommand = new RelayCommand(ClearQueue, CanClearQueue);
        _clearOutputDirectoryCommand = new RelayCommand(ClearOutputDirectory, CanClearOutputDirectory);
        _removeImportItemCommand = new RelayCommand(RemoveImportItem, CanRemoveImportItem);
        _cancelExecutionCommand = new RelayCommand(CancelExecution, () => IsBusy);
        _toggleSettingsPaneCommand = new RelayCommand(ToggleSettingsPane);
        _closeSettingsPaneCommand = new RelayCommand(CloseSettingsPane, () => IsSettingsPaneOpen);
        _showMediaDetailsCommand = new RelayCommand(OpenMediaDetails, CanOpenMediaDetails);
        _closeMediaDetailsCommand = new RelayCommand(CloseMediaDetails, CanCloseMediaDetails);

        DetailPanel.PropertyChanged += OnDetailPanelPropertyChanged;

        _selectedProcessingMode = ResolveProcessingMode(userPreferences.PreferredProcessingMode);
        ReloadOutputFormats(userPreferences.PreferredOutputFormatExtension);
    }

    public ObservableCollection<LogEntry> LogEntries { get; }

    public ObservableCollection<MediaJobViewModel> ImportItems { get; }

    public ObservableCollection<OutputFormatOption> AvailableOutputFormats { get; }

    public IReadOnlyList<ProcessingModeOption> ProcessingModes { get; }

    public IReadOnlyList<ThemePreferenceOption> ThemeOptions { get; }

    public MediaDetailPanelViewModel DetailPanel { get; }

    public ICommand SelectFilesCommand => _selectFilesCommand;

    public ICommand SelectFolderCommand => _selectFolderCommand;

    public ICommand SelectOutputDirectoryCommand => _selectOutputDirectoryCommand;

    public ICommand ClearQueueCommand => _clearQueueCommand;

    public ICommand ClearOutputDirectoryCommand => _clearOutputDirectoryCommand;

    public ICommand RemoveImportItemCommand => _removeImportItemCommand;

    public ICommand ExecuteProcessingCommand => _executeProcessingCommand;

    public ICommand CancelExecutionCommand => _cancelExecutionCommand;

    public ICommand ToggleSettingsPaneCommand => _toggleSettingsPaneCommand;

    public ICommand CloseSettingsPaneCommand => _closeSettingsPaneCommand;

    public ICommand ShowMediaDetailsCommand => _showMediaDetailsCommand;

    public ICommand CloseMediaDetailsCommand => _closeMediaDetailsCommand;

    public bool IsSettingsPaneOpen
    {
        get => _isSettingsPaneOpen;
        set
        {
            if (SetProperty(ref _isSettingsPaneOpen, value))
            {
                _closeSettingsPaneCommand.NotifyCanExecuteChanged();

                // Resume deferred detail opening here so we do not depend only on the view event.
                if (!value)
                {
                    HandleSettingsPaneClosed();
                }
            }
        }
    }

    public ProcessingModeOption SelectedProcessingMode
    {
        get => _selectedProcessingMode ?? ProcessingModes.First();
        set
        {
            if (value is null)
            {
                return;
            }

            if (SetProperty(ref _selectedProcessingMode, value))
            {
                ReloadOutputFormats();
                RecalculatePlannedOutputs();
                PersistUserPreferences();
            }
        }
    }

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
                RecalculatePlannedOutputs();
                PersistUserPreferences();
            }
        }
    }

    public string OutputDirectory
    {
        get => _outputDirectory;
        set
        {
            var normalizedDirectory = NormalizeOutputDirectory(value);

            if (SetProperty(ref _outputDirectory, normalizedDirectory))
            {
                OnPropertyChanged(nameof(HasCustomOutputDirectory));
                RecalculatePlannedOutputs();
                PersistUserPreferences();
                _clearOutputDirectoryCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool HasCustomOutputDirectory => !string.IsNullOrWhiteSpace(OutputDirectory);

    public ThemePreferenceOption SelectedThemeOption
    {
        get => _selectedThemeOption ?? ThemeOptions[0];
        set
        {
            if (value is null)
            {
                return;
            }

            if (SetProperty(ref _selectedThemeOption, value))
            {
                OnPropertyChanged(nameof(RequestedTheme));
                PersistUserPreferences();
            }
        }
    }

    public bool RevealOutputFileAfterProcessing
    {
        get => _revealOutputFileAfterProcessing;
        set
        {
            if (SetProperty(ref _revealOutputFileAfterProcessing, value))
            {
                PersistUserPreferences();
            }
        }
    }

    public ElementTheme RequestedTheme => ConvertThemePreferenceToElementTheme(SelectedThemeOption.Preference);

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(CanModifyInputs));
                NotifyCommandStates();
            }
        }
    }

    public bool CanModifyInputs => !IsBusy;


    public string QueueSummaryText => ImportItems.Count switch
    {
        0 => "等待导入",
        1 => "1 个文件",
        _ => $"{ImportItems.Count} 个文件"
    };

    public string SupportedInputFormatsHint =>
        "\u652F\u6301\u5BFC\u5165\u683C\u5F0F\uFF08" +
        string.Join("\u3001", _configuration.SupportedInputFileTypes.Select(extension => extension.TrimStart('.').ToUpperInvariant())) +
        "\uFF09";

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        ImportItems.CollectionChanged -= OnImportItemsChanged;
        DetailPanel.PropertyChanged -= OnDetailPanelPropertyChanged;

        CancelDetailLoad();
        _detailLoadCancellationSource?.Dispose();
        _detailLoadCancellationSource = null;

        _executionCancellationSource?.Cancel();
        _executionCancellationSource?.Dispose();
        _executionCancellationSource = null;
    }

    private void OnDetailPanelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MediaDetailPanelViewModel.IsOpen))
        {
            NotifyCommandStates();
        }
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await EnsureRuntimeReadyAsync(cancellationToken);

        if (!IsBusy && !string.IsNullOrWhiteSpace(_runtimeExecutablePath))
        {
            SetReadyStatusMessage();
        }
    }

    public async Task ImportPathsAsync(IEnumerable<string> inputPaths)
    {
        ArgumentNullException.ThrowIfNull(inputPaths);

        if (IsBusy)
        {
            StatusMessage = "当前正在处理任务，请等待完成或先取消。";
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

        StatusMessage = "正在整理导入内容...";

        var discovery = await Task.Run(() => _mediaImportDiscoveryService.Discover(normalizedPaths));
        var knownPaths = new HashSet<string>(ImportItems.Select(item => item.InputPath), StringComparer.OrdinalIgnoreCase);
        var addedCount = 0;
        var duplicateCount = 0;

        foreach (var filePath in discovery.SupportedFiles)
        {
            if (!knownPaths.Add(filePath))
            {
                duplicateCount++;
                continue;
            }

            var item = new MediaJobViewModel(filePath);
            item.UpdatePlannedOutputPath(CreateOutputPath(filePath));
            ImportItems.Add(item);
            addedCount++;
        }

        StatusMessage = CreateImportStatusMessage(addedCount, duplicateCount, discovery);

        if (discovery.UnavailableDirectories > 0)
        {
            _logger.Log(LogLevel.Warning, $"有 {discovery.UnavailableDirectories} 个文件夹无法访问，已跳过。");
        }
    }

    private async Task SelectFilesAsync()
    {
        try
        {
            var selectedFiles = await _filePickerService.PickFilesAsync(
                new FilePickerRequest(_configuration.SupportedInputFileTypes, "导入视频文件"));

            if (selectedFiles.Count == 0)
            {
                StatusMessage = "已取消文件导入。";
                return;
            }

            await ImportPathsAsync(selectedFiles);
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "已取消文件导入。";
        }
        catch (Exception exception)
        {
            StatusMessage = "导入文件失败。";
            _logger.Log(LogLevel.Error, "导入文件时发生异常。", exception);
        }
    }

    private async Task SelectFolderAsync()
    {
        try
        {
            var selectedFolder = await _filePickerService.PickFolderAsync("导入视频文件夹");

            if (string.IsNullOrWhiteSpace(selectedFolder))
            {
                StatusMessage = "已取消文件夹导入。";
                return;
            }

            await ImportPathsAsync(new[] { selectedFolder });
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "已取消文件夹导入。";
        }
        catch (Exception exception)
        {
            StatusMessage = "导入文件夹失败。";
            _logger.Log(LogLevel.Error, "导入文件夹时发生异常。", exception);
        }
    }

    private async Task SelectOutputDirectoryAsync()
    {
        try
        {
            var selectedFolder = await _filePickerService.PickFolderAsync("选择输出目录");

            if (string.IsNullOrWhiteSpace(selectedFolder))
            {
                StatusMessage = "已取消选择输出目录。";
                return;
            }

            OutputDirectory = selectedFolder;
            StatusMessage = $"已将输出目录设置为：{OutputDirectory}";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "已取消选择输出目录。";
        }
        catch (Exception exception)
        {
            StatusMessage = "选择输出目录失败。";
            _logger.Log(LogLevel.Error, "选择输出目录时发生异常。", exception);
        }
    }

    private async Task ExecuteProcessingAsync()
    {
        if (ImportItems.Count == 0)
        {
            StatusMessage = "请先导入至少一个视频文件。";
            AddUiLog(LogLevel.Warning, "请先导入至少一个视频文件。", clearExisting: false);
            return;
        }

        if (!await EnsureRuntimeReadyAsync(logUiFailure: true))
        {
            return;
        }

        _executionCancellationSource?.Dispose();
        _executionCancellationSource = new CancellationTokenSource();

        var batchStartedAt = DateTimeOffset.UtcNow;

        try
        {
            IsSettingsPaneOpen = false;
            IsBusy = true;
            EnsureOutputDirectoryExists();
            RecalculatePlannedOutputs();
            ClearUiLogs();

            foreach (var item in ImportItems)
            {
                item.ResetStatus();
            }

            var successCount = 0;
            var failedCount = 0;
            var cancelledCount = 0;
            string? lastSuccessfulOutputPath = null;
            StatusMessage = $"开始处理 {ImportItems.Count} 个文件...";
            AddUiLog(LogLevel.Info, $"开始处理 {ImportItems.Count} 个文件。", clearExisting: false);

            for (var index = 0; index < ImportItems.Count; index++)
            {
                var item = ImportItems[index];

                if (_executionCancellationSource.IsCancellationRequested)
                {
                    item.MarkCancelled();
                    cancelledCount++;
                    continue;
                }

                item.MarkRunning();

                var command = BuildCommand(item.InputPath, item.PlannedOutputPath);
                var executionOptions = new FFmpegExecutionOptions
                {
                    Timeout = _configuration.DefaultExecutionTimeout
                };

                var result = await _ffmpegService.ExecuteAsync(
                    command,
                    executionOptions,
                    _executionCancellationSource.Token);

                var elapsedText = FormatDuration(result.Duration);

                if (result.WasSuccessful && File.Exists(item.PlannedOutputPath))
                {
                    item.MarkSucceeded($"用时 {elapsedText}");
                    successCount++;
                    lastSuccessfulOutputPath = item.PlannedOutputPath;
                    AddUiLog(LogLevel.Info, $"{item.InputFileName} 处理成功，用时 {elapsedText}。", clearExisting: false);
                    continue;
                }

                if (result.WasCancelled)
                {
                    item.MarkCancelled();
                    cancelledCount++;
                    cancelledCount += MarkRemainingItemsCancelled(index + 1);
                    AddUiLog(LogLevel.Warning, "任务已取消，未完成的文件已停止处理。", clearExisting: false);
                    break;
                }

                var failureMessage = CreateFriendlyFailureMessage(result);
                item.MarkFailed($"原因：{failureMessage}");
                failedCount++;
                AddUiLog(LogLevel.Error, $"{item.InputFileName} 处理失败，用时 {elapsedText}。原因：{failureMessage}", clearExisting: false);
            }

            var wasCancelled = _executionCancellationSource.IsCancellationRequested;

            StatusMessage = wasCancelled
                ? $"任务已取消，成功 {successCount} 个，失败 {failedCount} 个，取消 {cancelledCount} 个。"
                : failedCount == 0
                    ? $"全部处理完成，共成功 {successCount} 个文件。"
                    : $"处理完成，成功 {successCount} 个，失败 {failedCount} 个。";

            AddUiLog(
                wasCancelled || failedCount > 0 ? LogLevel.Warning : LogLevel.Info,
                CreateBatchSummaryMessage(successCount, failedCount, cancelledCount, DateTimeOffset.UtcNow - batchStartedAt, wasCancelled),
                clearExisting: false);

            RevealLastSuccessfulOutputIfNeeded(lastSuccessfulOutputPath, successCount, wasCancelled);
        }
        catch (Exception exception)
        {
            StatusMessage = "处理过程中发生异常。";
            _logger.Log(LogLevel.Error, "批量处理流程被异常中断。", exception);
            AddUiLog(LogLevel.Error, $"批量处理被中断。原因：{exception.Message}", clearExisting: false);
        }
        finally
        {
            IsBusy = false;
            _executionCancellationSource?.Dispose();
            _executionCancellationSource = null;
        }
    }

    private void ToggleSettingsPane()
    {
        var shouldOpen = !IsSettingsPaneOpen;
        if (shouldOpen)
        {
            _pendingDetailItem = null;
            CloseMediaDetails();
        }

        IsSettingsPaneOpen = shouldOpen;
    }

    private void CloseSettingsPane()
    {
        _pendingDetailItem = null;
        IsSettingsPaneOpen = false;
    }

    private void OpenMediaDetails(object? parameter)
    {
        if (parameter is not MediaJobViewModel item || !ImportItems.Contains(item))
        {
            return;
        }

        if (IsSettingsPaneOpen)
        {
            _pendingDetailItem = item;
            IsSettingsPaneOpen = false;
            return;
        }

        _pendingDetailItem = null;
        _ = OpenMediaDetailsAsync(item);
    }

    private async Task OpenMediaDetailsAsync(MediaJobViewModel item)
    {
        var inputPath = item.InputPath;
        var title = item.InputFileName;

        CancelDetailLoad();
        var detailLoadVersion = Interlocked.Increment(ref _detailLoadVersion);
        IsSettingsPaneOpen = false;

        if (_mediaInfoService.TryGetCachedDetails(inputPath, out var cachedSnapshot))
        {
            if (!IsCurrentDetailLoadVersion(detailLoadVersion))
            {
                return;
            }

            DetailPanel.ShowDetails(cachedSnapshot);
            StatusMessage = $"已从缓存载入 {item.InputFileName} 的详情。";
            NotifyCommandStates();
            return;
        }

        if (!IsCurrentDetailLoadVersion(detailLoadVersion))
        {
            return;
        }

        DetailPanel.ShowLoading(title, inputPath);
        StatusMessage = $"正在解析 {item.InputFileName} 的视频详情...";
        NotifyCommandStates();

        var detailLoadCancellationSource = new CancellationTokenSource();
        _detailLoadCancellationSource = detailLoadCancellationSource;

        try
        {
            var result = await _mediaInfoService.GetMediaDetailsAsync(inputPath, detailLoadCancellationSource.Token).ConfigureAwait(false);
            if (!IsCurrentDetailLoadVersion(detailLoadVersion))
            {
                return;
            }

            _dispatcherService.TryEnqueue(() =>
            {
                if (!IsCurrentDetailLoadVersion(detailLoadVersion))
                {
                    return;
                }

                if (result.IsSuccess && result.Snapshot is not null)
                {
                    DetailPanel.ShowDetails(result.Snapshot);
                    StatusMessage = $"视频详情已加载：{item.InputFileName}";
                    return;
                }

                var errorMessage = result.ErrorMessage ?? "无法解析该视频文件。";
                DetailPanel.ShowError(title, inputPath, errorMessage);
                StatusMessage = errorMessage;
            });
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            _logger.Log(LogLevel.Error, "读取视频详情时发生异常。", exception);
            if (!IsCurrentDetailLoadVersion(detailLoadVersion))
            {
                return;
            }

            var errorMessage = ExtractFriendlyExceptionMessage(exception);
            _dispatcherService.TryEnqueue(() =>
            {
                if (!IsCurrentDetailLoadVersion(detailLoadVersion))
                {
                    return;
                }

                DetailPanel.ShowError(title, inputPath, errorMessage);
                StatusMessage = errorMessage;
            });
        }
        finally
        {
            if (ReferenceEquals(_detailLoadCancellationSource, detailLoadCancellationSource))
            {
                _detailLoadCancellationSource = null;
            }

            detailLoadCancellationSource.Dispose();
            _dispatcherService.TryEnqueue(NotifyCommandStates);
        }
    }

    private void CloseMediaDetails()
    {
        CancelDetailLoad();
        DetailPanel.Close();
        NotifyCommandStates();
    }

    private void CancelDetailLoad()
    {
        Interlocked.Increment(ref _detailLoadVersion);
        _detailLoadCancellationSource?.Cancel();
    }

    private void CloseMediaDetailsIfShowing(string inputPath)
    {
        if (!DetailPanel.IsOpen || string.IsNullOrWhiteSpace(inputPath))
        {
            return;
        }

        if (string.Equals(DetailPanel.CurrentInputPath, inputPath, StringComparison.OrdinalIgnoreCase))
        {
            CloseMediaDetails();
        }
    }

    public void HandleSettingsPaneClosed()
    {
        if (_pendingDetailItem is not { } item)
        {
            return;
        }

        _pendingDetailItem = null;

        if (!ImportItems.Contains(item))
        {
            return;
        }

        _ = OpenMediaDetailsAsync(item);
    }

    private void ClearQueue()
    {
        if (ImportItems.Count == 0)
        {
            return;
        }

        CloseMediaDetails();
        ImportItems.Clear();
        StatusMessage = "已清空待处理列表。";
    }

    private void ClearOutputDirectory()
    {
        if (!HasCustomOutputDirectory)
        {
            return;
        }

        OutputDirectory = string.Empty;
        StatusMessage = "已清空输出目录，留空时将使用原文件夹输出。";
    }

    private void RemoveImportItem(object? parameter)
    {
        if (parameter is not MediaJobViewModel item || !ImportItems.Remove(item))
        {
            return;
        }

        CloseMediaDetailsIfShowing(item.InputPath);
        StatusMessage = $"已从待处理列表移除 {item.InputFileName}。";
    }

    private void CancelExecution()
    {
        if (!IsBusy)
        {
            return;
        }

        StatusMessage = "正在取消当前任务...";
        _executionCancellationSource?.Cancel();
    }

    private bool CanClearQueue() => !IsBusy && ImportItems.Count > 0;

    private bool CanClearOutputDirectory() => CanModifyInputs && HasCustomOutputDirectory;

    private bool CanRemoveImportItem(object? parameter) =>
        !IsBusy &&
        parameter is MediaJobViewModel item &&
        ImportItems.Contains(item);

    private bool CanOpenMediaDetails(object? parameter) =>
        parameter is MediaJobViewModel item &&
        ImportItems.Contains(item);

    private bool CanCloseMediaDetails() => DetailPanel.IsOpen;

    private bool CanExecuteProcessing() =>
        !IsBusy &&
        !DetailPanel.IsOpen &&
        ImportItems.Count > 0 &&
        _selectedOutputFormat is not null;

    private bool IsCurrentDetailLoadVersion(int detailLoadVersion) =>
        Volatile.Read(ref _detailLoadVersion) == detailLoadVersion;

    private async Task<bool> EnsureRuntimeReadyAsync(CancellationToken cancellationToken = default, bool logUiFailure = false)
    {
        if (!string.IsNullOrWhiteSpace(_runtimeExecutablePath) && File.Exists(_runtimeExecutablePath))
        {
            return true;
        }

        try
        {
            IsBusy = true;
            StatusMessage = RuntimePreparingMessage;

            var resolution = await _ffmpegRuntimeService.EnsureAvailableAsync(cancellationToken);
            _runtimeExecutablePath = resolution.ExecutablePath;


            SetReadyStatusMessage();

            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            StatusMessage = RuntimePreparationCancelledMessage;

            if (logUiFailure)
            {
                AddUiLog(LogLevel.Warning, RuntimePreparationCancelledMessage, clearExisting: false);
            }

            return false;
        }
        catch (Exception exception)
        {
            StatusMessage = RuntimePreparationFailedMessage;
            _logger.Log(LogLevel.Error, "准备本地 FFmpeg 时发生异常。", exception);

            if (logUiFailure)
            {
                var reason = ExtractFriendlyExceptionMessage(exception);
                AddUiLog(LogLevel.Error, $"运行环境未就绪，无法开始处理。原因：{reason}", clearExisting: false);
            }

            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private FFmpegCommand BuildCommand(string inputPath, string outputPath)
    {
        if (string.IsNullOrWhiteSpace(_runtimeExecutablePath))
        {
            throw new InvalidOperationException("运行环境尚未准备完成。");
        }

        IFFmpegCommandBuilder builder = _ffmpegCommandBuilder
            .Reset()
            .SetExecutablePath(_runtimeExecutablePath)
            .AddGlobalParameter("-hide_banner")
            .SetInput(inputPath)
            .SetOutput(outputPath);

        builder = builder.AddGlobalParameter(_configuration.OverwriteOutputFiles ? "-y" : "-n");

        return SelectedProcessingMode.Mode switch
        {
            ProcessingMode.VideoConvert => BuildVideoOutputCommand(
                builder
                    .AddParameter("-map", "0:v")
                    .AddParameter("-map", "0:a?")
                    .AddParameter("-sn")
                    .AddParameter("-dn"),
                includeAudio: true),
            ProcessingMode.VideoTrackExtract => BuildVideoOutputCommand(
                builder
                    .AddParameter("-map", "0:v")
                    .AddParameter("-an")
                    .AddParameter("-sn")
                    .AddParameter("-dn"),
                includeAudio: false),
            ProcessingMode.AudioTrackExtract => BuildAudioExtractionCommand(builder),
            _ => throw new InvalidOperationException("不支持的处理模式。")
        };
    }

    private FFmpegCommand BuildVideoOutputCommand(IFFmpegCommandBuilder builder, bool includeAudio)
    {
        var extension = SelectedOutputFormat.Extension.ToLowerInvariant();

        return extension switch
        {
            ".mp4" => builder
                .AddParameter("-c", "copy")
                .AddParameter("-movflags", "+faststart")
                .Build(),
            ".mkv" => builder
                .AddParameter("-c", "copy")
                .Build(),
            ".mov" => builder
                .AddParameter("-c", "copy")
                .AddParameter("-movflags", "+faststart")
                .Build(),
            ".avi" => BuildAviOutputCommand(builder, includeAudio),
            ".wmv" => BuildWmvOutputCommand(builder, includeAudio),
            ".m4v" => builder
                .AddParameter("-c", "copy")
                .AddParameter("-f", "mp4")
                .AddParameter("-movflags", "+faststart")
                .Build(),
            ".flv" => BuildFlvOutputCommand(builder, includeAudio),
            ".webm" => BuildWebMOutputCommand(builder, includeAudio),
            ".ts" => builder
                .AddParameter("-c", "copy")
                .AddParameter("-f", "mpegts")
                .Build(),
            ".m2ts" => builder
                .AddParameter("-c", "copy")
                .AddParameter("-f", "mpegts")
                .AddParameter("-mpegts_m2ts_mode", "1")
                .Build(),
            ".mpeg" => BuildMpegOutputCommand(builder, includeAudio),
            ".mpg" => BuildMpegOutputCommand(builder, includeAudio),
            _ => throw new InvalidOperationException("不支持的视频输出格式。")
        };
    }

    private static FFmpegCommand BuildAviOutputCommand(IFFmpegCommandBuilder builder, bool includeAudio)
    {
        builder = builder
            .AddParameter("-c:v", "mpeg4")
            .AddParameter("-q:v", "2")
            .AddParameter("-pix_fmt", "yuv420p");

        if (includeAudio)
        {
            builder = builder
                .AddParameter("-c:a", "libmp3lame")
                .AddParameter("-q:a", "2");
        }

        return builder.Build();
    }

    private static FFmpegCommand BuildWmvOutputCommand(IFFmpegCommandBuilder builder, bool includeAudio)
    {
        builder = builder
            .AddParameter("-c:v", "wmv2")
            .AddParameter("-b:v", "4M")
            .AddParameter("-pix_fmt", "yuv420p");

        if (includeAudio)
        {
            builder = builder
                .AddParameter("-c:a", "wmav2")
                .AddParameter("-b:a", "192k");
        }

        return builder.Build();
    }

    private static FFmpegCommand BuildFlvOutputCommand(IFFmpegCommandBuilder builder, bool includeAudio)
    {
        builder = builder
            .AddParameter("-c:v", "flv")
            .AddParameter("-b:v", "3M")
            .AddParameter("-pix_fmt", "yuv420p");

        if (includeAudio)
        {
            builder = builder
                .AddParameter("-c:a", "libmp3lame")
                .AddParameter("-b:a", "192k");
        }

        return builder.Build();
    }

    private static FFmpegCommand BuildWebMOutputCommand(IFFmpegCommandBuilder builder, bool includeAudio)
    {
        builder = builder
            .AddParameter("-c:v", "libvpx-vp9")
            .AddParameter("-crf", "32")
            .AddParameter("-b:v", "0")
            .AddParameter("-pix_fmt", "yuv420p");

        if (includeAudio)
        {
            builder = builder
                .AddParameter("-c:a", "libopus")
                .AddParameter("-b:a", "160k");
        }

        return builder.Build();
    }

    private static FFmpegCommand BuildMpegOutputCommand(IFFmpegCommandBuilder builder, bool includeAudio)
    {
        builder = builder
            .AddParameter("-c:v", "mpeg2video")
            .AddParameter("-q:v", "2")
            .AddParameter("-pix_fmt", "yuv420p")
            .AddParameter("-f", "mpeg");

        if (includeAudio)
        {
            builder = builder
                .AddParameter("-c:a", "mp2")
                .AddParameter("-b:a", "192k");
        }

        return builder.Build();
    }

    private FFmpegCommand BuildAudioExtractionCommand(IFFmpegCommandBuilder builder)
    {
        builder = builder
            .AddParameter("-map", "0:a:0")
            .AddParameter("-vn")
            .AddParameter("-sn")
            .AddParameter("-dn");

        var extension = SelectedOutputFormat.Extension.ToLowerInvariant();

        builder = extension switch
        {
            ".mp3" => builder
                .AddParameter("-c:a", "libmp3lame")
                .AddParameter("-q:a", "2"),
            ".m4a" => builder
                .AddParameter("-c:a", "aac")
                .AddParameter("-b:a", "256k")
                .AddParameter("-movflags", "+faststart"),
            ".aac" => builder
                .AddParameter("-c:a", "aac")
                .AddParameter("-b:a", "256k"),
            ".wav" => builder
                .AddParameter("-c:a", "pcm_s16le"),
            ".flac" => builder
                .AddParameter("-c:a", "flac"),
            _ => throw new InvalidOperationException("不支持的音频输出格式。")
        };

        return builder.Build();
    }

    private void PersistUserPreferences()
    {
        var existingPreferences = _userPreferencesService.Load();

        _userPreferencesService.Save(new UserPreferences
        {
            PreferredProcessingMode = _selectedProcessingMode?.Mode,
            PreferredOutputFormatExtension = _selectedOutputFormat?.Extension,
            PreferredOutputDirectory = HasCustomOutputDirectory ? OutputDirectory : null,
            ThemePreference = SelectedThemeOption.Preference,
            RevealOutputFileAfterProcessing = RevealOutputFileAfterProcessing,
            MainWindowPlacement = existingPreferences.MainWindowPlacement
        });
    }

    private void RevealLastSuccessfulOutputIfNeeded(string? outputPath, int successCount, bool wasCancelled)
    {
        if (!RevealOutputFileAfterProcessing ||
            wasCancelled ||
            successCount == 0 ||
            string.IsNullOrWhiteSpace(outputPath))
        {
            return;
        }

        try
        {
            _fileRevealService.RevealFile(outputPath);
            AddUiLog(
                LogLevel.Info,
                successCount == 1
                    ? "已打开输出文件所在文件夹，并选中处理完成的文件。"
                    : "已打开最后一个成功输出文件所在文件夹，并选中该输出文件。",
                clearExisting: false);
        }
        catch (Exception exception)
        {
            _logger.Log(LogLevel.Warning, "打开输出文件所在位置失败。", exception);
            AddUiLog(LogLevel.Warning, $"处理已完成，但未能打开输出文件所在位置。原因：{ExtractFriendlyExceptionMessage(exception)}", clearExisting: false);
        }
    }

    private void ReloadOutputFormats(string? preferredOutputFormatExtension = null)
    {
        AvailableOutputFormats.Clear();

        var formats = SelectedProcessingMode.Mode == ProcessingMode.AudioTrackExtract
            ? _configuration.SupportedAudioOutputFormats
            : _configuration.SupportedVideoOutputFormats;

        foreach (var format in formats)
        {
            AvailableOutputFormats.Add(format);
        }

        var desiredExtension = preferredOutputFormatExtension ?? _selectedOutputFormat?.Extension;
        var preferredFormat = desiredExtension is null
            ? null
            : AvailableOutputFormats.FirstOrDefault(format => string.Equals(format.Extension, desiredExtension, StringComparison.OrdinalIgnoreCase));

        if (preferredFormat is not null)
        {
            if (!ReferenceEquals(_selectedOutputFormat, preferredFormat))
            {
                _selectedOutputFormat = preferredFormat;
                OnPropertyChanged(nameof(SelectedOutputFormat));
            }

            return;
        }

        if (_selectedOutputFormat is null ||
            !AvailableOutputFormats.Any(format => string.Equals(format.Extension, _selectedOutputFormat.Extension, StringComparison.OrdinalIgnoreCase)))
        {
            _selectedOutputFormat = AvailableOutputFormats.FirstOrDefault();
            OnPropertyChanged(nameof(SelectedOutputFormat));
        }
    }

    private ProcessingModeOption ResolveProcessingMode(ProcessingMode? preferredProcessingMode)
    {
        if (preferredProcessingMode is ProcessingMode processingMode)
        {
            var matchingMode = ProcessingModes.FirstOrDefault(option => option.Mode == processingMode);
            if (matchingMode is not null)
            {
                return matchingMode;
            }
        }

        return ProcessingModes.First();
    }

    private void RecalculatePlannedOutputs()
    {
        if (_selectedOutputFormat is null)
        {
            return;
        }

        var usedOutputPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in ImportItems)
        {
            var plannedOutputPath = CreateUniqueOutputPath(CreateOutputPath(item.InputPath), usedOutputPaths);
            item.UpdatePlannedOutputPath(plannedOutputPath);
        }
    }

    private string CreateOutputPath(string inputPath) => SelectedProcessingMode.Mode switch
    {
        ProcessingMode.VideoConvert => MediaPathResolver.CreateVideoConversionOutputPath(inputPath, SelectedOutputFormat.Extension, GetEffectiveOutputDirectory()),
        ProcessingMode.VideoTrackExtract => MediaPathResolver.CreateVideoTrackOutputPath(inputPath, SelectedOutputFormat.Extension, GetEffectiveOutputDirectory()),
        ProcessingMode.AudioTrackExtract => MediaPathResolver.CreateAudioTrackOutputPath(inputPath, SelectedOutputFormat.Extension, GetEffectiveOutputDirectory()),
        _ => throw new InvalidOperationException("不支持的处理模式。")
    };

    private string? GetEffectiveOutputDirectory() =>
        HasCustomOutputDirectory
            ? OutputDirectory
            : null;

    private string NormalizeOutputDirectory(string? outputDirectory)
    {
        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            return string.Empty;
        }

        try
        {
            return Path.GetFullPath(outputDirectory.Trim());
        }
        catch (Exception exception) when (
            exception is ArgumentException or
            NotSupportedException or
            PathTooLongException)
        {
            _logger.Log(LogLevel.Warning, "检测到无效的输出目录配置，已回退为原文件夹输出。", exception);
            return string.Empty;
        }
    }

    private void EnsureOutputDirectoryExists()
    {
        if (!HasCustomOutputDirectory)
        {
            return;
        }

        Directory.CreateDirectory(OutputDirectory);
    }

    private static string CreateUniqueOutputPath(string outputPath, ISet<string> usedOutputPaths)
    {
        var directory = Path.GetDirectoryName(outputPath)
            ?? throw new InvalidOperationException("输出路径缺少有效目录。");
        var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(outputPath);
        var extension = Path.GetExtension(outputPath);
        var candidatePath = outputPath;
        var suffixIndex = 2;

        while (usedOutputPaths.Contains(candidatePath) || File.Exists(candidatePath) || Directory.Exists(candidatePath))
        {
            candidatePath = Path.Combine(directory, $"{fileNameWithoutExtension}_{suffixIndex}{extension}");
            suffixIndex++;
        }

        usedOutputPaths.Add(candidatePath);
        return candidatePath;
    }

    private string CreateImportStatusMessage(int addedCount, int duplicateCount, MediaImportDiscoveryResult discovery)
    {
        if (addedCount > 0)
        {
            return $"已导入 {addedCount} 个视频文件。";
        }

        if (duplicateCount > 0)
        {
            return "导入内容已存在于列表中。";
        }

        return discovery.UnsupportedEntries > 0 || discovery.MissingEntries > 0
            ? "没有发现可处理的视频文件。"
            : "未发现新的可处理文件。";
    }

    private string CreateFriendlyFailureMessage(FFmpegExecutionResult result)
    {
        var standardError = result.StandardError;

        if (result.TimedOut)
        {
            return "处理超时。";
        }

        if (standardError.Contains("matches no streams", StringComparison.OrdinalIgnoreCase))
        {
            return SelectedProcessingMode.Mode switch
            {
                ProcessingMode.VideoTrackExtract => "该文件没有可提取的视频轨道。",
                ProcessingMode.AudioTrackExtract => "该文件没有可提取的音频轨道。",
                _ => "该文件缺少可处理的媒体流。"
            };
        }

        if (standardError.Contains("not currently supported in container", StringComparison.OrdinalIgnoreCase) ||
            standardError.Contains("Could not write header", StringComparison.OrdinalIgnoreCase))
        {
            return "目标格式与当前媒体流不兼容，请尝试 MP4、MKV、MOV 或更换其他导出格式。";
        }

        if (standardError.Contains("Permission denied", StringComparison.OrdinalIgnoreCase))
        {
            return "输出文件正在被占用，或当前目录没有写入权限。";
        }

        if (standardError.Contains("Unknown encoder", StringComparison.OrdinalIgnoreCase))
        {
            return "当前环境缺少所需格式支持，无法输出该格式。";
        }

        if (standardError.Contains("Invalid argument", StringComparison.OrdinalIgnoreCase))
        {
            return "当前输出格式或参数无效，无法完成处理。";
        }

        if (standardError.Contains("No such file or directory", StringComparison.OrdinalIgnoreCase))
        {
            return "输入文件不存在，或输出目录不可用。";
        }

        var extractedReason = TryExtractMeaningfulErrorLine(standardError);
        if (!string.IsNullOrWhiteSpace(extractedReason))
        {
            return extractedReason;
        }

        return result.FailureReason ?? "处理失败。";
    }

    private int MarkRemainingItemsCancelled(int startIndex)
    {
        var cancelledCount = 0;

        for (var index = startIndex; index < ImportItems.Count; index++)
        {
            var item = ImportItems[index];
            if (!item.IsPending)
            {
                continue;
            }

            item.MarkCancelled();
            cancelledCount++;
        }

        return cancelledCount;
    }

    private void NotifyCommandStates()
    {
        _selectFilesCommand.NotifyCanExecuteChanged();
        _selectFolderCommand.NotifyCanExecuteChanged();
        _selectOutputDirectoryCommand.NotifyCanExecuteChanged();
        _clearQueueCommand.NotifyCanExecuteChanged();
        _clearOutputDirectoryCommand.NotifyCanExecuteChanged();
        _removeImportItemCommand.NotifyCanExecuteChanged();
        _showMediaDetailsCommand.NotifyCanExecuteChanged();
        _executeProcessingCommand.NotifyCanExecuteChanged();
        _cancelExecutionCommand.NotifyCanExecuteChanged();
        _closeMediaDetailsCommand.NotifyCanExecuteChanged();
    }


    private void SetReadyStatusMessage()
    {
        StatusMessage = ImportItems.Count == 0
            ? ReadyForImportMessage
            : ReadyForProcessingMessage;
    }

    private void OnImportItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(QueueSummaryText));
        RecalculatePlannedOutputs();
        NotifyCommandStates();
    }

    private void AddUiLog(LogLevel level, string message, bool clearExisting)
    {
        void UpdateLogEntries()
        {
            if (clearExisting)
            {
                LogEntries.Clear();
            }

            LogEntries.Insert(0, new LogEntry(DateTimeOffset.Now, level, message));

            if (LogEntries.Count > 200)
            {
                LogEntries.RemoveAt(LogEntries.Count - 1);
            }
        }

        if (_dispatcherService.HasThreadAccess)
        {
            UpdateLogEntries();
            return;
        }

        _dispatcherService.TryEnqueue(UpdateLogEntries);
    }

    private void ClearUiLogs()
    {
        if (_dispatcherService.HasThreadAccess)
        {
            LogEntries.Clear();
            return;
        }

        _dispatcherService.TryEnqueue(() => LogEntries.Clear());
    }

    private static ElementTheme ConvertThemePreferenceToElementTheme(ThemePreference preference) => preference switch
    {
        ThemePreference.Light => ElementTheme.Light,
        ThemePreference.Dark => ElementTheme.Dark,
        _ => ElementTheme.Default
    };

    private static string CreateBatchSummaryMessage(
        int successCount,
        int failedCount,
        int cancelledCount,
        TimeSpan totalDuration,
        bool wasCancelled)
    {
        var summary = wasCancelled
            ? $"任务已取消，成功 {successCount} 个，失败 {failedCount} 个，取消 {cancelledCount} 个"
            : failedCount == 0
                ? $"处理完成，成功 {successCount} 个文件"
                : $"处理完成，成功 {successCount} 个，失败 {failedCount} 个";

        return $"{summary}，总用时 {FormatDuration(totalDuration)}。";
    }

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalMinutes >= 1)
        {
            var totalMinutes = (int)duration.TotalMinutes;
            return duration.Seconds == 0
                ? $"{totalMinutes} 分钟"
                : $"{totalMinutes} 分 {duration.Seconds} 秒";
        }

        return $"{Math.Max(duration.TotalSeconds, 0.1):F1} 秒";
    }

    private static string ExtractFriendlyExceptionMessage(Exception exception)
    {
        return string.IsNullOrWhiteSpace(exception.Message)
            ? "请检查网络连接或运行时目录。"
            : exception.Message;
    }

    private static string? TryExtractMeaningfulErrorLine(string standardError)
    {
        if (string.IsNullOrWhiteSpace(standardError))
        {
            return null;
        }

        var lines = standardError
            .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        for (var index = lines.Length - 1; index >= 0; index--)
        {
            var line = lines[index];
            if (string.IsNullOrWhiteSpace(line) || IsIgnorableErrorLine(line))
            {
                continue;
            }

            if (line.StartsWith("[", StringComparison.OrdinalIgnoreCase))
            {
                var closingBracketIndex = line.LastIndexOf(']');
                if (closingBracketIndex >= 0 && closingBracketIndex < line.Length - 1)
                {
                    line = line[(closingBracketIndex + 1)..].Trim();
                }
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            return line.Length > 120
                ? $"{line[..117]}..."
                : line.TrimEnd('.');
        }

        return null;
    }

    private static bool IsIgnorableErrorLine(string line)
    {
        return line.StartsWith("frame=", StringComparison.OrdinalIgnoreCase) ||
               line.StartsWith("size=", StringComparison.OrdinalIgnoreCase) ||
               line.StartsWith("time=", StringComparison.OrdinalIgnoreCase) ||
               line.StartsWith("video:", StringComparison.OrdinalIgnoreCase) ||
               line.StartsWith("audio:", StringComparison.OrdinalIgnoreCase) ||
               line.StartsWith("subtitle:", StringComparison.OrdinalIgnoreCase) ||
               line.StartsWith("Input #", StringComparison.OrdinalIgnoreCase) ||
               line.StartsWith("Output #", StringComparison.OrdinalIgnoreCase) ||
               line.StartsWith("Stream mapping:", StringComparison.OrdinalIgnoreCase) ||
               line.StartsWith("Metadata:", StringComparison.OrdinalIgnoreCase) ||
               line.StartsWith("Duration:", StringComparison.OrdinalIgnoreCase) ||
               line.StartsWith("Press [q]", StringComparison.OrdinalIgnoreCase) ||
               line.StartsWith("configuration:", StringComparison.OrdinalIgnoreCase) ||
               line.StartsWith("libav", StringComparison.OrdinalIgnoreCase);
    }
}
