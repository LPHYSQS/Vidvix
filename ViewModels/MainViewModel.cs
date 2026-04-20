// 功能：主工作区视图模型（协调视频转换、音频转换与裁剪工作区的界面状态）
// 模块：视频转换模块 / 音频转换模块 / 裁剪模块
// 说明：可复用，负责状态与绑定，不直接承载底层 FFmpeg 业务实现。
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
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

    private readonly ApplicationConfiguration _configuration;
    private readonly IMediaInfoService _mediaInfoService;
    private readonly IVideoThumbnailService _videoThumbnailService;
    private readonly IMediaProcessingWorkflowService _mediaProcessingWorkflowService;
    private readonly IMediaImportDiscoveryService _mediaImportDiscoveryService;
    private readonly ILocalizationService _localizationService;
    private readonly ILogger _logger;
    private readonly IFilePickerService _filePickerService;
    private readonly IDispatcherService _dispatcherService;
    private readonly IUserPreferencesService _userPreferencesService;
    private readonly IFileRevealService _fileRevealService;
    private readonly IDesktopShortcutService _desktopShortcutService;
    private readonly ObservableCollection<LogEntry> _videoLogEntries;
    private readonly ObservableCollection<LogEntry> _audioLogEntries;
    private readonly ObservableCollection<LogEntry> _trimLogEntries;
    private readonly ObservableCollection<LogEntry> _mergeLogEntries;
    private readonly ObservableCollection<LogEntry> _splitAudioLogEntries;
    private readonly ObservableCollection<LogEntry> _terminalLogEntries;
    private readonly ObservableCollection<MediaJobViewModel> _videoImportItems;
    private readonly ObservableCollection<MediaJobViewModel> _audioImportItems;
    private readonly ObservableCollection<MediaJobViewModel> _trimImportItems;
    private readonly ObservableCollection<MediaJobViewModel> _mergeImportItems;
    private readonly ObservableCollection<MediaJobViewModel> _splitAudioImportItems;
    private readonly ObservableCollection<MediaJobViewModel> _terminalImportItems;
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
    private readonly AsyncRelayCommand _createDesktopShortcutCommand;
    private readonly RelayCommand _showMediaDetailsCommand;
    private readonly RelayCommand _closeMediaDetailsCommand;
    private readonly RelayCommand _copyAllMediaDetailsCommand;
    private readonly RelayCommand _copyMediaDetailSectionCommand;
    private readonly AsyncRelayCommand _switchToVideoWorkspaceCommand;
    private readonly AsyncRelayCommand _switchToAudioWorkspaceCommand;
    private readonly AsyncRelayCommand _switchToTrimWorkspaceCommand;
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
    private bool _enableSystemTray;
    private TranscodingModeOption? _selectedTranscodingModeOption;
    private bool _enableGpuAccelerationForTranscoding;
    private bool _isSettingsPaneOpen;
    private string _desktopShortcutNotificationMessage = string.Empty;
    private InfoBarSeverity _desktopShortcutNotificationSeverity = InfoBarSeverity.Informational;
    private bool _isDesktopShortcutNotificationOpen;
    private bool _isDisposed;
    private ProcessingWorkspaceKind _selectedWorkspaceKind;

    internal MainViewModel(
        MainViewModelDependencies dependencies,
        VideoTrimWorkspaceViewModel trimWorkspace,
        MergeViewModel mergeWorkspace,
        SplitAudioWorkspaceViewModel splitAudioWorkspace,
        TerminalWorkspaceViewModel terminalWorkspace)
    {
        ArgumentNullException.ThrowIfNull(dependencies);

        _configuration = dependencies.Configuration;
        _mediaInfoService = dependencies.MediaInfoService;
        _videoThumbnailService = dependencies.VideoThumbnailService;
        _mediaProcessingWorkflowService = dependencies.MediaProcessingWorkflowService;
        _mediaImportDiscoveryService = dependencies.MediaImportDiscoveryService;
        _localizationService = dependencies.LocalizationService;
        _logger = dependencies.Logger;
        _filePickerService = dependencies.FilePickerService;
        _dispatcherService = dependencies.DispatcherService;
        _userPreferencesService = dependencies.UserPreferencesService;
        _fileRevealService = dependencies.FileRevealService;
        _desktopShortcutService = dependencies.DesktopShortcutService;
        TrimWorkspace = trimWorkspace ?? throw new ArgumentNullException(nameof(trimWorkspace));
        MergeWorkspace = mergeWorkspace ?? throw new ArgumentNullException(nameof(mergeWorkspace));
        SplitAudioWorkspace = splitAudioWorkspace ?? throw new ArgumentNullException(nameof(splitAudioWorkspace));
        TerminalWorkspace = terminalWorkspace ?? throw new ArgumentNullException(nameof(terminalWorkspace));
        _statusMessage = RuntimePreparingMessage;

        _videoLogEntries = new ObservableCollection<LogEntry>();
        _audioLogEntries = new ObservableCollection<LogEntry>();
        _trimLogEntries = new ObservableCollection<LogEntry>();
        _mergeLogEntries = new ObservableCollection<LogEntry>();
        _splitAudioLogEntries = new ObservableCollection<LogEntry>();
        _terminalLogEntries = new ObservableCollection<LogEntry>();
        _videoImportItems = new ObservableCollection<MediaJobViewModel>();
        _audioImportItems = new ObservableCollection<MediaJobViewModel>();
        _trimImportItems = new ObservableCollection<MediaJobViewModel>();
        _mergeImportItems = new ObservableCollection<MediaJobViewModel>();
        _splitAudioImportItems = new ObservableCollection<MediaJobViewModel>();
        _terminalImportItems = new ObservableCollection<MediaJobViewModel>();
        DetailPanel = new MediaDetailPanelViewModel();
        ProcessingModes = BuildProcessingModes();

        var userPreferences = _userPreferencesService.Load();
        InitializeLocalizationState(userPreferences.CurrentUiLanguage);
        _selectedWorkspaceKind = ResolvePreferredWorkspaceKind(userPreferences.PreferredWorkspaceKind);
        InitializePreferredOutputFormatSelections(userPreferences);
        _selectedThemeOption = ThemeOptions.FirstOrDefault(option => option.Preference == userPreferences.ThemePreference) ?? ThemeOptions[0];
        _outputDirectory = NormalizeOutputDirectory(userPreferences.PreferredOutputDirectory);
        _revealOutputFileAfterProcessing = userPreferences.RevealOutputFileAfterProcessing;
        _enableSystemTray = userPreferences.EnableSystemTray;
        _selectedTranscodingModeOption = ResolveTranscodingMode(userPreferences.PreferredTranscodingMode);
        _enableGpuAccelerationForTranscoding = userPreferences.EnableGpuAccelerationForTranscoding;

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
        _createDesktopShortcutCommand = new AsyncRelayCommand(CreateDesktopShortcutAsync);
        _showMediaDetailsCommand = new RelayCommand(OpenMediaDetails, CanOpenMediaDetails);
        _closeMediaDetailsCommand = new RelayCommand(CloseMediaDetails, CanCloseMediaDetails);
        _copyAllMediaDetailsCommand = new RelayCommand(CopyAllMediaDetails, CanCopyAllMediaDetails);
        _copyMediaDetailSectionCommand = new RelayCommand(CopyMediaDetailSection, CanCopyMediaDetailSection);
        _switchToVideoWorkspaceCommand = new AsyncRelayCommand(SwitchToVideoWorkspaceAsync, () => CanModifyInputs);
        _switchToAudioWorkspaceCommand = new AsyncRelayCommand(SwitchToAudioWorkspaceAsync, () => CanModifyInputs);
        _switchToTrimWorkspaceCommand = new AsyncRelayCommand(SwitchToTrimWorkspaceAsync, () => CanModifyInputs);
        _switchToMergeWorkspaceCommand = new AsyncRelayCommand(SwitchToMergeWorkspaceAsync, () => CanModifyInputs);
        _switchToSplitAudioWorkspaceCommand = new AsyncRelayCommand(SwitchToSplitAudioWorkspaceAsync, () => CanModifyInputs);
        _switchToTerminalWorkspaceCommand = new AsyncRelayCommand(SwitchToTerminalWorkspaceAsync, () => CanModifyInputs);

        _localizationService.LanguageChanged += OnLocalizationLanguageChanged;
        DetailPanel.PropertyChanged += OnDetailPanelPropertyChanged;
        TrimWorkspace.PropertyChanged += OnTrimWorkspacePropertyChanged;
        MergeWorkspace.PropertyChanged += OnMergeWorkspacePropertyChanged;
        SplitAudioWorkspace.PropertyChanged += OnSplitAudioWorkspacePropertyChanged;

        _selectedProcessingMode = ResolveProcessingMode(userPreferences.PreferredProcessingMode);
        ReloadOutputFormats();
    }

    public IReadOnlyList<OutputFormatOption> AvailableOutputFormats
    {
        get => _availableOutputFormats;
        private set => SetProperty(ref _availableOutputFormats, value);
    }

    public IReadOnlyList<ProcessingModeOption> ProcessingModes
    {
        get => _processingModes;
        private set => SetProperty(ref _processingModes, value);
    }

    public IReadOnlyList<ThemePreferenceOption> ThemeOptions
    {
        get => _themeOptions;
        private set => SetProperty(ref _themeOptions, value);
    }

    public IReadOnlyList<TranscodingModeOption> TranscodingOptions
    {
        get => _transcodingOptions;
        private set => SetProperty(ref _transcodingOptions, value);
    }

    public MediaDetailPanelViewModel DetailPanel { get; }

    public VideoTrimWorkspaceViewModel TrimWorkspace { get; }

    public SplitAudioWorkspaceViewModel SplitAudioWorkspace { get; }

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

    public ICommand CreateDesktopShortcutCommand => _createDesktopShortcutCommand;

    public ICommand ShowMediaDetailsCommand => _showMediaDetailsCommand;

    public ICommand CloseMediaDetailsCommand => _closeMediaDetailsCommand;

    public ICommand CopyAllMediaDetailsCommand => _copyAllMediaDetailsCommand;

    public ICommand CopyMediaDetailSectionCommand => _copyMediaDetailSectionCommand;

    public ICommand SwitchToVideoWorkspaceCommand => _switchToVideoWorkspaceCommand;

    public ICommand SwitchToAudioWorkspaceCommand => _switchToAudioWorkspaceCommand;

    public ICommand SwitchToTrimWorkspaceCommand => _switchToTrimWorkspaceCommand;

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
            // ComboBox.SelectedItem must point at the exact instance in the current ItemsSource.
            var formats = AvailableOutputFormats.Count > 0
                ? AvailableOutputFormats
                : GetOutputFormatsForMode(outputPreferenceMode);
            if (_selectedOutputFormat is not null)
            {
                var matchingFormat = formats.FirstOrDefault(format => string.Equals(format.Extension, _selectedOutputFormat.Extension, StringComparison.OrdinalIgnoreCase));
                if (matchingFormat is not null)
                {
                    return matchingFormat;
                }
            }

            return ResolvePreferredOutputFormat(outputPreferenceMode, formats, preferredExtension: null);
        }
        set
        {
            if (value is null)
            {
                return;
            }

            if (SetProperty(ref _selectedOutputFormat, value))
            {
                OnPropertyChanged(nameof(SelectedOutputFormatDescription));
                RememberOutputFormatSelection(GetCurrentOutputFormatPreferenceMode(), value.Extension);
                RecalculatePlannedOutputs();
                PersistUserPreferences();
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

    public TranscodingModeOption SelectedTranscodingModeOption
    {
        get => _selectedTranscodingModeOption ?? TranscodingOptions[0];
        set
        {
            if (value is null)
            {
                return;
            }

            if (SetProperty(ref _selectedTranscodingModeOption, value))
            {
                OnPropertyChanged(nameof(IsFullTranscodingModeSelected));
                OnPropertyChanged(nameof(GpuAccelerationSettingVisibility));
                OnPropertyChanged(nameof(GpuAccelerationDescription));
                PersistUserPreferences();
            }
        }
    }

    public bool EnableGpuAccelerationForTranscoding
    {
        get => _enableGpuAccelerationForTranscoding;
        set
        {
            if (SetProperty(ref _enableGpuAccelerationForTranscoding, value))
            {
                OnPropertyChanged(nameof(GpuAccelerationDescription));
                PersistUserPreferences();
            }
        }
    }

    public bool IsFullTranscodingModeSelected => SelectedTranscodingModeOption.Mode == TranscodingMode.FullTranscode;

    public string DesktopShortcutNotificationMessage
    {
        get => _desktopShortcutNotificationMessage;
        private set => SetProperty(ref _desktopShortcutNotificationMessage, value);
    }

    public InfoBarSeverity DesktopShortcutNotificationSeverity
    {
        get => _desktopShortcutNotificationSeverity;
        private set => SetProperty(ref _desktopShortcutNotificationSeverity, value);
    }

    public bool IsDesktopShortcutNotificationOpen
    {
        get => _isDesktopShortcutNotificationOpen;
        set => SetProperty(ref _isDesktopShortcutNotificationOpen, value);
    }

    public Visibility GpuAccelerationSettingVisibility =>
        IsFullTranscodingModeSelected ? Visibility.Visible : Visibility.Collapsed;

    public string GpuAccelerationDescription =>
        EnableGpuAccelerationForTranscoding
            ? GetLocalizedText(
                "settings.transcoding.gpu.description.enabled",
                "开启后，会先检测当前电脑是否存在可用的 GPU 视频硬件编码能力；若不适用或不可用，会自动回退为 CPU 转码，不会影响任务继续执行。音频任务、字幕任务与部分旧式视频格式仍会继续使用 CPU。")
            : GetLocalizedText(
                "settings.transcoding.gpu.description.disabled",
                "关闭后，真正转码会始终使用 CPU 重新编码，速度更稳定，也不会额外检测显卡能力。");

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

    public bool CanModifyInputs =>
        !IsBusy &&
        !TrimWorkspace.IsBusy &&
        !MergeWorkspace.IsVideoJoinProcessing &&
        !SplitAudioWorkspace.IsBusy;

    public string QueueSummaryText => ImportItems.Count switch
    {
        0 => "等待导入",
        1 => "1 个文件",
        _ => $"{ImportItems.Count} 个文件"
    };

    public string SupportedInputFormatsHint =>
        GetCurrentWorkspaceProfile().SupportedInputFormatsHint;

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _localizationService.LanguageChanged -= OnLocalizationLanguageChanged;
        _videoImportItems.CollectionChanged -= OnImportItemsChanged;
        _audioImportItems.CollectionChanged -= OnImportItemsChanged;
        TrimWorkspace.PropertyChanged -= OnTrimWorkspacePropertyChanged;
        MergeWorkspace.PropertyChanged -= OnMergeWorkspacePropertyChanged;
        SplitAudioWorkspace.PropertyChanged -= OnSplitAudioWorkspacePropertyChanged;
        DetailPanel.PropertyChanged -= OnDetailPanelPropertyChanged;
        TrimWorkspace.Dispose();
        SplitAudioWorkspace.Dispose();
        TerminalWorkspace.Dispose();

        CancelDetailLoad();
        _detailLoadCancellationSource?.Dispose();
        _detailLoadCancellationSource = null;

        _executionCancellationSource?.Cancel();
        _executionCancellationSource?.Dispose();
        _executionCancellationSource = null;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        SynchronizeLocalizationStateWithService();
        await EnsureRuntimeReadyAsync(cancellationToken);

        if (!IsBusy && !string.IsNullOrWhiteSpace(_runtimeExecutablePath))
        {
            SetReadyStatusMessage();
        }
    }

    private void OnDetailPanelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MediaDetailPanelViewModel.IsOpen) ||
            e.PropertyName == nameof(MediaDetailPanelViewModel.HasContent) ||
            e.PropertyName == nameof(MediaDetailPanelViewModel.VideoOverviewVisibility) ||
            e.PropertyName == nameof(MediaDetailPanelViewModel.VideoInfoVisibility) ||
            e.PropertyName == nameof(MediaDetailPanelViewModel.AudioInfoVisibility) ||
            e.PropertyName == nameof(MediaDetailPanelViewModel.AdvancedVisibility) ||
            e.PropertyName == nameof(MediaDetailPanelViewModel.AudioOverviewVisibility))
        {
            NotifyCommandStates();
        }
    }

    private void OnTrimWorkspacePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(VideoTrimWorkspaceViewModel.IsBusy))
        {
            OnPropertyChanged(nameof(CanModifyInputs));
            NotifyCommandStates();
        }
    }

    private void OnMergeWorkspacePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MergeViewModel.IsVideoJoinProcessing))
        {
            OnPropertyChanged(nameof(CanModifyInputs));
            NotifyCommandStates();
        }
    }

    public bool EnableSystemTray
    {
        get => _enableSystemTray;
        set
        {
            if (SetProperty(ref _enableSystemTray, value))
            {
                PersistUserPreferences();
                StatusMessage = value
                    ? GetLocalizedText(
                        "settings.systemTray.status.enabled",
                        "已启用系统托盘，点击关闭按钮后会隐藏到托盘中继续运行。")
                    : GetLocalizedText(
                        "settings.systemTray.status.disabled",
                        "已关闭系统托盘，点击关闭按钮将直接退出应用。");
            }
        }
    }

    private void OnSplitAudioWorkspacePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SplitAudioWorkspaceViewModel.IsBusy))
        {
            OnPropertyChanged(nameof(CanModifyInputs));
            NotifyCommandStates();
        }
    }
}
