using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.UI.Xaml;
using Vidvix.Core.Interfaces;
using Vidvix.Core.Models;
using Vidvix.Utils;

namespace Vidvix.ViewModels;

/// <summary>
/// 协调主界面的长期状态、命令和服务调用。
/// 具体流程按导入、详情、处理、偏好等职责拆分到 partial 文件中，便于长期维护和 AI 工具快速定位。
/// </summary>
public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    private const string RuntimePreparingMessage = "正在准备运行环境...";
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
    private readonly ObservableCollection<LogEntry> _videoLogEntries;
    private readonly ObservableCollection<LogEntry> _audioLogEntries;
    private readonly ObservableCollection<MediaJobViewModel> _videoImportItems;
    private readonly ObservableCollection<MediaJobViewModel> _audioImportItems;
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
    private readonly RelayCommand _switchToVideoWorkspaceCommand;
    private readonly RelayCommand _switchToAudioWorkspaceCommand;
    private readonly Dictionary<ProcessingMode, string> _preferredOutputFormatExtensionsByMode = new();

    private string? _runtimeExecutablePath;
    private string _statusMessage;
    private bool _isBusy;
    private CancellationTokenSource? _executionCancellationSource;
    private CancellationTokenSource? _detailLoadCancellationSource;
    private int _detailLoadVersion;
    private MediaJobViewModel? _pendingDetailItem;
    private ProcessingModeOption? _selectedProcessingMode;
    private OutputFormatOption? _selectedOutputFormat;
    private IReadOnlyList<OutputFormatOption> _availableOutputFormats = Array.Empty<OutputFormatOption>();
    private string _outputDirectory = string.Empty;
    private ThemePreferenceOption? _selectedThemeOption;
    private bool _revealOutputFileAfterProcessing;
    private bool _isSettingsPaneOpen;
    private bool _isDisposed;
    private ProcessingWorkspaceKind _selectedWorkspaceKind;

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
        _videoLogEntries = new ObservableCollection<LogEntry>();
        _audioLogEntries = new ObservableCollection<LogEntry>();
        _videoImportItems = new ObservableCollection<MediaJobViewModel>();
        _audioImportItems = new ObservableCollection<MediaJobViewModel>();
        DetailPanel = new MediaDetailPanelViewModel();
        ProcessingModes = _configuration.SupportedProcessingModes;

        var userPreferences = _userPreferencesService.Load();
        _selectedWorkspaceKind = ResolvePreferredWorkspaceKind(userPreferences.PreferredWorkspaceKind);
        InitializePreferredOutputFormatSelections(userPreferences);
        _selectedThemeOption = ThemeOptions.FirstOrDefault(option => option.Preference == userPreferences.ThemePreference) ?? ThemeOptions[0];
        _outputDirectory = NormalizeOutputDirectory(userPreferences.PreferredOutputDirectory);
        _revealOutputFileAfterProcessing = userPreferences.RevealOutputFileAfterProcessing;

        _videoImportItems.CollectionChanged += OnImportItemsChanged;
        _audioImportItems.CollectionChanged += OnImportItemsChanged;

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
        _switchToVideoWorkspaceCommand = new RelayCommand(SwitchToVideoWorkspace, () => CanModifyInputs);
        _switchToAudioWorkspaceCommand = new RelayCommand(SwitchToAudioWorkspace, () => CanModifyInputs);

        DetailPanel.PropertyChanged += OnDetailPanelPropertyChanged;

        _selectedProcessingMode = ResolveProcessingMode(userPreferences.PreferredProcessingMode);
        ReloadOutputFormats();
    }

    public IReadOnlyList<OutputFormatOption> AvailableOutputFormats
    {
        get => _availableOutputFormats;
        private set => SetProperty(ref _availableOutputFormats, value);
    }

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

    public ICommand SwitchToVideoWorkspaceCommand => _switchToVideoWorkspaceCommand;

    public ICommand SwitchToAudioWorkspaceCommand => _switchToAudioWorkspaceCommand;

    public bool IsSettingsPaneOpen
    {
        get => _isSettingsPaneOpen;
        set
        {
            if (SetProperty(ref _isSettingsPaneOpen, value))
            {
                _closeSettingsPaneCommand.NotifyCanExecuteChanged();

                // 在这里恢复延迟打开详情，避免仅依赖视图事件触发。
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
        get
        {
            var outputPreferenceMode = GetCurrentOutputFormatPreferenceMode();
            var formats = GetOutputFormatsForMode(outputPreferenceMode);
            if (_selectedOutputFormat is not null)
            {
                var matchingFormat = formats.FirstOrDefault(format => string.Equals(format.Extension, _selectedOutputFormat.Extension, StringComparison.OrdinalIgnoreCase));
                if (matchingFormat is not null)
                {
                    return matchingFormat;
                }
            }

            return ResolvePreferredOutputFormat(outputPreferenceMode);
        }
        set
        {
            if (value is null)
            {
                return;
            }

            if (SetProperty(ref _selectedOutputFormat, value))
            {
                RememberOutputFormatSelection(GetCurrentOutputFormatPreferenceMode(), value.Extension);
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
        "支持导入格式（" +
        string.Join("、", GetCurrentSupportedInputFileTypes().Select(extension => extension.TrimStart('.').ToUpperInvariant())) +
        "）";

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _videoImportItems.CollectionChanged -= OnImportItemsChanged;
        _audioImportItems.CollectionChanged -= OnImportItemsChanged;
        DetailPanel.PropertyChanged -= OnDetailPanelPropertyChanged;

        CancelDetailLoad();
        _detailLoadCancellationSource?.Dispose();
        _detailLoadCancellationSource = null;

        _executionCancellationSource?.Cancel();
        _executionCancellationSource?.Dispose();
        _executionCancellationSource = null;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await EnsureRuntimeReadyAsync(cancellationToken);

        if (!IsBusy && !string.IsNullOrWhiteSpace(_runtimeExecutablePath))
        {
            SetReadyStatusMessage();
        }
    }

    private void OnDetailPanelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MediaDetailPanelViewModel.IsOpen))
        {
            NotifyCommandStates();
        }
    }

    private readonly record struct ProcessingExecutionContext(
        ProcessingWorkspaceKind WorkspaceKind,
        ProcessingMode ProcessingMode,
        OutputFormatOption OutputFormat);
}
