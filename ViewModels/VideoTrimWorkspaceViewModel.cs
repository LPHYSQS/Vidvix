using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Vidvix.Core.Interfaces;
using Vidvix.Core.Models;
using Vidvix.Utils;

// 功能：视频裁剪工作区视图模型（管理导入状态、裁剪区间与导出进度）
// 模块：裁剪模块
// 说明：可复用，负责状态与绑定，不直接承载底层 FFmpeg 业务实现。
namespace Vidvix.ViewModels;

public sealed partial class VideoTrimWorkspaceViewModel : ObservableObject, IDisposable
{
    private static readonly TimeSpan MinimumSelectionLength = TimeSpan.FromMilliseconds(1);
    private const double DefaultTrimPreviewVolumePercent = 80d;

    private readonly ApplicationConfiguration _configuration;
    private readonly ILocalizationService _localizationService;
    private readonly ITrimWorkflowService _trimWorkflowService;
    private readonly IFilePickerService _filePickerService;
    private readonly IUserPreferencesService _userPreferencesService;
    private readonly IFileRevealService _fileRevealService;
    private readonly ILogger _logger;
    private readonly AsyncRelayCommand _selectVideoCommand;
    private readonly RelayCommand _removeVideoCommand;
    private readonly AsyncRelayCommand _selectOutputDirectoryCommand;
    private readonly RelayCommand _clearOutputDirectoryCommand;
    private readonly AsyncRelayCommand _exportTrimCommand;
    private readonly RelayCommand _cancelExportCommand;

    private string _statusMessage = string.Empty;
    private string _previewStateMessage = string.Empty;
    private IReadOnlyList<OutputFormatOption> _availableOutputFormats = Array.Empty<OutputFormatOption>();
    private IReadOnlyList<TranscodingModeOption> _transcodingOptions = Array.Empty<TranscodingModeOption>();
    private OutputFormatOption? _selectedOutputFormat;
    private TranscodingModeOption? _selectedTranscodingModeOption;
    private string _inputPath = string.Empty;
    private string _inputFileName = string.Empty;
    private MediaDetailsSnapshot? _mediaDetailsSnapshot;
    private IReadOnlyList<MediaDetailField> _basicInfoFields = Array.Empty<MediaDetailField>();
    private IReadOnlyList<MediaDetailField> _videoInfoFields = Array.Empty<MediaDetailField>();
    private IReadOnlyList<MediaDetailField> _audioInfoFields = Array.Empty<MediaDetailField>();
    private string _outputDirectory = string.Empty;
    private string _plannedOutputPath = string.Empty;
    private string _lastImportErrorDetails = string.Empty;
    private TimeSpan _mediaDuration;
    private TimeSpan _selectionStart;
    private TimeSpan _selectionEnd;
    private TimeSpan _currentPosition;
    private string _selectionStartInputText = string.Empty;
    private string _selectionEndInputText = string.Empty;
    private TrimMediaKind _currentMediaKind = TrimMediaKind.Video;
    private double _volume = DefaultTrimPreviewVolumePercent / 100d;
    private bool _isBusy;
    private bool _isPreviewReady;
    private bool _isPlaying;
    private bool _isSelectionStartInputEditing;
    private bool _isSelectionEndInputEditing;
    private CancellationTokenSource? _exportCancellationSource;
    private Visibility _exportProgressVisibility = Visibility.Collapsed;
    private bool _isExportProgressIndeterminate;
    private double _exportProgressValue;
    private string _exportProgressSummaryText = string.Empty;
    private string _exportProgressDetailText = string.Empty;
    private string _exportProgressPercentText = string.Empty;
    private Func<string>? _statusMessageResolver;
    private Func<string>? _previewStateMessageResolver;
    private Action? _exportProgressStateApplier;
    private bool _isDisposed;

    internal VideoTrimWorkspaceViewModel(VideoTrimWorkspaceDependencies dependencies)
    {
        ArgumentNullException.ThrowIfNull(dependencies);

        _configuration = dependencies.Configuration;
        _localizationService = dependencies.LocalizationService;
        _trimWorkflowService = dependencies.TrimWorkflowService;
        _filePickerService = dependencies.FilePickerService;
        _userPreferencesService = dependencies.UserPreferencesService;
        _fileRevealService = dependencies.FileRevealService;
        _videoPreviewService = dependencies.VideoPreviewService;
        _dispatcherService = dependencies.DispatcherService;
        _logger = dependencies.Logger;

        var preferences = _userPreferencesService.Load();
        _availableOutputFormats = ResolveOutputFormats(_currentMediaKind);
        _transcodingOptions = BuildTranscodingOptions();
        _selectedOutputFormat = ResolvePreferredOutputFormat(preferences.PreferredTrimOutputFormatExtension);
        _selectedTranscodingModeOption = ResolvePreferredTranscodingMode(preferences.PreferredTrimTranscodingMode);
        _outputDirectory = NormalizeOutputDirectory(preferences.PreferredTrimOutputDirectory);
        _volume = ResolvePreferredVolumePercent(preferences.PreferredTrimPreviewVolumePercent) / 100d;
        SetStatusMessage(() => GetLocalizedText(
            "trim.status.ready",
            "\u8bf7\u5bfc\u5165\u97f3\u9891\u6216\u89c6\u9891\u6587\u4ef6\uff0c\u4e5f\u53ef\u62d6\u62fd\u5230\u6b64\u5904\u5f00\u59cb\u88c1\u526a\u3002"));
        SetPreviewStateMessage(() => GetLocalizedText(
            "trim.status.needsInput",
            "\u8bf7\u5148\u5bfc\u5165\u4e00\u4e2a\u6587\u4ef6\u3002"));

        _selectVideoCommand = new AsyncRelayCommand(SelectVideoAsync, () => !HasInput && !IsBusy);
        _removeVideoCommand = new RelayCommand(RemoveVideo, () => HasInput && !IsBusy);
        _selectOutputDirectoryCommand = new AsyncRelayCommand(SelectOutputDirectoryAsync, () => HasInput && !IsBusy);
        _clearOutputDirectoryCommand = new RelayCommand(ClearOutputDirectory, () => HasCustomOutputDirectory && !IsBusy);
        _exportTrimCommand = new AsyncRelayCommand(ExportTrimAsync, CanExportTrim);
        _cancelExportCommand = new RelayCommand(CancelExport, () => IsBusy);
        InitializePreview();
    }

    public IReadOnlyList<OutputFormatOption> AvailableOutputFormats => _availableOutputFormats;

    public IReadOnlyList<TranscodingModeOption> TranscodingOptions => _transcodingOptions;

    public ICommand SelectVideoCommand => _selectVideoCommand;

    public ICommand RemoveVideoCommand => _removeVideoCommand;

    public ICommand SelectOutputDirectoryCommand => _selectOutputDirectoryCommand;

    public ICommand ClearOutputDirectoryCommand => _clearOutputDirectoryCommand;

    public ICommand ExportTrimCommand => _exportTrimCommand;

    public ICommand CancelExportCommand => _cancelExportCommand;

    public bool IsAudioTrim => HasInput && _currentMediaKind == TrimMediaKind.Audio;

    public bool IsVideoTrim => HasInput && _currentMediaKind == TrimMediaKind.Video;

    public string EditorTitle => IsAudioTrim
        ? GetLocalizedText("trim.editor.title.audio", "\u97f3\u9891\u88c1\u526a")
        : GetLocalizedText("trim.editor.title.video", "\u89c6\u9891\u88c1\u526a");

    public string EditorDescription => GetLocalizedText(
        "trim.editor.description",
        "\u5bfc\u5165\u540e\u4f1a\u5728\u540c\u4e00\u5f20\u4e3b\u5361\u7247\u4e2d\u663e\u793a\u9884\u89c8\u3001\u65f6\u95f4\u8f74\u3001\u88c1\u526a\u8303\u56f4\u4e0e\u5bfc\u51fa\u8bbe\u7f6e\u3002");

    public string CurrentFileCaptionText => GetLocalizedText(
        "trim.editor.currentFileCaption",
        "\u5f53\u524d\u6587\u4ef6");

    public Symbol PlaceholderIconSymbol => Symbol.OpenFile;

    public string PlaceholderTitleText => GetLocalizedText(
        "trim.placeholder.title",
        "\u8bf7\u5bfc\u5165\u6587\u4ef6\u6216\u62d6\u62fd\u5230\u6b64\u5904\u5f00\u59cb\u88c1\u526a");

    public string PlaceholderDescriptionText => GetLocalizedText(
        "trim.placeholder.description",
        "\u5bfc\u5165\u540e\u4f1a\u5728\u540c\u4e00\u5f20\u4e3b\u5361\u7247\u4e2d\u663e\u793a\u6587\u4ef6\u9884\u89c8\u3001\u64ad\u653e\u63a7\u5236\u3001\u65f6\u95f4\u8f74\u3001\u53cc\u6ed1\u5757\u88c1\u526a\u548c\u8f93\u51fa\u683c\u5f0f\u9009\u62e9\u3002");

    public string ImportErrorDetailsTitle => GetLocalizedText(
        "trim.placeholder.importErrorTitle",
        "\u5bfc\u5165\u5931\u8d25\u8be6\u60c5");

    public string RemoveInputCommandText => GetLocalizedText(
        "trim.action.removeInput",
        "\u79fb\u9664\u6587\u4ef6");

    public string SelectionRuleDescription =>
        SelectedTranscodingModeOption.Mode == TranscodingMode.FullTranscode
            ? GetLocalizedText(
                "trim.selection.rule.accurate",
                "\u7cbe\u786e\u5ea6\u4f18\u5148\u4f1a\u4e25\u683c\u6309\u5165\u70b9\u548c\u51fa\u70b9\u5bfc\u51fa\uff1b\u89c6\u9891\u4f1a\u4f18\u5148\u5c1d\u8bd5\u667a\u80fd\u88c1\u526a\uff0c\u5fc5\u8981\u65f6\u81ea\u52a8\u56de\u9000\u4e3a\u6574\u6bb5\u7cbe\u786e\u91cd\u7f16\u7801\u3002")
            : GetLocalizedText(
                "trim.selection.rule.fast",
                "\u901f\u5ea6\u4f18\u5148\u4f1a\u5728\u5bb9\u5668\u517c\u5bb9\u65f6\u4f18\u5148\u590d\u7528\u539f\u59cb\u6d41\uff0c\u5bfc\u51fa\u66f4\u5feb\uff0c\u4f46\u975e\u5173\u952e\u5e27\u8fb9\u754c\u53ef\u80fd\u4e0e\u9884\u89c8\u5b58\u5728\u8f7b\u5fae\u504f\u5dee\u3002");

    public string OutputDirectoryHintText => GetLocalizedText(
        "trim.settings.outputDirectoryHint",
        "\u7559\u7a7a\u65f6\u9ed8\u8ba4\u5bfc\u51fa\u5230\u539f\u6587\u4ef6\u6240\u5728\u6587\u4ef6\u5939");

    public string MediaInfoPanelTitle => IsAudioTrim
        ? GetLocalizedText("trim.mediaInfo.title.audio", "\u97f3\u9891\u4fe1\u606f")
        : GetLocalizedText("trim.mediaInfo.title.video", "\u89c6\u9891\u4fe1\u606f");

    public Visibility AudioPreviewSurfaceVisibility => HasInput &&
        IsAudioTrim &&
        _mediaDetailsSnapshot?.HasEmbeddedArtwork != true
        ? Visibility.Visible
        : Visibility.Collapsed;

    public string AudioPreviewTitleText
    {
        get
        {
            if (string.IsNullOrWhiteSpace(_inputFileName))
            {
                return GetLocalizedText("trim.preview.audioUntitled", "\u672a\u547d\u540d\u97f3\u9891");
            }

            var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(_inputFileName);
            return string.IsNullOrWhiteSpace(fileNameWithoutExtension)
                ? _inputFileName
                : fileNameWithoutExtension;
        }
    }

    public string AudioPreviewSubtitleText => string.IsNullOrWhiteSpace(_inputFileName)
        ? GetLocalizedText("trim.preview.audioFileType", "\u97f3\u9891\u6587\u4ef6")
        : _inputFileName;

    public string AudioPreviewFormatBadgeText
    {
        get
        {
            var extension = Path.GetExtension(_inputFileName);
            return string.IsNullOrWhiteSpace(extension)
                ? GetLocalizedText("trim.preview.audioBadgeFallback", "AUDIO")
                : extension.TrimStart('.').ToUpperInvariant();
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public string PreviewStateMessage
    {
        get => _previewStateMessage;
        private set => SetProperty(ref _previewStateMessage, value);
    }

    public bool HasInput => !string.IsNullOrWhiteSpace(_inputPath);

    public Visibility PlaceholderVisibility => HasInput ? Visibility.Collapsed : Visibility.Visible;

    public Visibility EditorVisibility => HasInput ? Visibility.Visible : Visibility.Collapsed;

    public bool CanEditSelectionInputs => HasInput && !IsBusy;

    public bool IsPreviewReady
    {
        get => _isPreviewReady;
        private set
        {
            if (SetProperty(ref _isPreviewReady, value))
            {
                OnPropertyChanged(nameof(CanPlayPreview));
                OnPropertyChanged(nameof(CanJumpToSelectionBoundary));
                OnPropertyChanged(nameof(PreviewOverlayVisibility));
            }
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(CanPlayPreview));
                OnPropertyChanged(nameof(CanJumpToSelectionBoundary));
                OnPropertyChanged(nameof(CanEditSelectionInputs));
                NotifyCommandStates();
            }
        }
    }

    public bool IsPlaying
    {
        get => _isPlaying;
        private set
        {
            if (SetProperty(ref _isPlaying, value))
            {
                OnPropertyChanged(nameof(CanJumpToSelectionBoundary));
                OnPropertyChanged(nameof(PlayPauseButtonText));
                OnPropertyChanged(nameof(PlayPauseButtonSymbol));
            }
        }
    }

    public bool CanPlayPreview => HasInput && IsPreviewReady && !IsBusy && !IsSeeking && _mediaDuration > TimeSpan.Zero;

    public bool CanJumpToSelectionBoundary => CanPlayPreview && !IsDragging;

    public Visibility PreviewOverlayVisibility => !IsPreviewReady ? Visibility.Visible : Visibility.Collapsed;

    public string PreviewOverlayMessage => PreviewStateMessage;

    public Symbol PreviewOverlaySymbol => IsAudioTrim ? Symbol.Audio : Symbol.Video;

    public string PlayPauseButtonText => IsPlaying
        ? GetLocalizedText("trim.preview.pause", "\u6682\u505c")
        : GetLocalizedText("trim.preview.play", "\u64ad\u653e");

    public Symbol PlayPauseButtonSymbol => IsPlaying ? Symbol.Pause : Symbol.Play;

    public string InputPath => _inputPath;

    public string InputFileName => _inputFileName;

    public IReadOnlyList<MediaDetailField> BasicInfoFields => _basicInfoFields;

    public IReadOnlyList<MediaDetailField> VideoInfoFields => _videoInfoFields;

    public IReadOnlyList<MediaDetailField> AudioInfoFields => _audioInfoFields;

    public Visibility BasicInfoVisibility => BasicInfoFields.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

    public Visibility VideoInfoVisibility => VideoInfoFields.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

    public Visibility AudioInfoVisibility => AudioInfoFields.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

    public string SupportedInputFormatsHint =>
        FormatLocalizedText(
            "trim.placeholder.supportedFormatsHint",
            "\u652f\u6301\u5bfc\u5165\u683c\u5f0f\uff08{formats}\uff09",
            ("formats", string.Join(
                GetLocalizedText("trim.common.listSeparator", "\u3001"),
                _configuration.SupportedTrimInputFileTypes.Select(item => item.TrimStart('.').ToUpperInvariant()))));

    public string DragDropCaptionText => GetLocalizedText(
        "trim.placeholder.dragDropCaption",
        "\u5bfc\u5165\u97f3\u9891\u6216\u89c6\u9891\u6587\u4ef6");

    public string LastImportErrorDetails
    {
        get => _lastImportErrorDetails;
        private set
        {
            if (SetProperty(ref _lastImportErrorDetails, value))
            {
                OnPropertyChanged(nameof(ImportErrorVisibility));
            }
        }
    }

    public Visibility ImportErrorVisibility =>
        string.IsNullOrWhiteSpace(LastImportErrorDetails) ? Visibility.Collapsed : Visibility.Visible;

    public OutputFormatOption SelectedOutputFormat
    {
        get => _selectedOutputFormat ?? _availableOutputFormats.First();
        set
        {
            if (value is not null && SetProperty(ref _selectedOutputFormat, value))
            {
                OnPropertyChanged(nameof(SelectedOutputFormatDescription));
                RefreshPlannedOutputPath();
                PersistPreferences();
                NotifyCommandStates();
            }
        }
    }

    public string SelectedOutputFormatDescription => SelectedOutputFormat.Description;

    public void RefreshLocalization()
    {
        var selectedExtension = _selectedOutputFormat?.Extension;
        var selectedTranscodingMode = _selectedTranscodingModeOption?.Mode ?? TranscodingMode.FastContainerConversion;
        _availableOutputFormats = ResolveOutputFormats(_currentMediaKind);
        _transcodingOptions = BuildTranscodingOptions();
        _selectedOutputFormat = ResolvePreferredOutputFormat(selectedExtension);
        _selectedTranscodingModeOption = ResolvePreferredTranscodingMode(selectedTranscodingMode);
        OnPropertyChanged(nameof(AvailableOutputFormats));
        OnPropertyChanged(nameof(SelectedOutputFormat));
        OnPropertyChanged(nameof(SelectedOutputFormatDescription));
        OnPropertyChanged(nameof(TranscodingOptions));
        OnPropertyChanged(nameof(SelectedTranscodingModeOption));
        OnPropertyChanged(nameof(SelectedTranscodingModeDescription));
        RefreshMediaInfoFields();
        RaiseTrimStateChanged();
        RefreshLocalizedRuntimeText();
    }

    public TranscodingModeOption SelectedTranscodingModeOption
    {
        get => _selectedTranscodingModeOption ?? _transcodingOptions[0];
        set
        {
            if (value is not null && SetProperty(ref _selectedTranscodingModeOption, value))
            {
                OnPropertyChanged(nameof(SelectedTranscodingModeDescription));
                OnPropertyChanged(nameof(SelectionRuleDescription));
                PersistPreferences();
                NotifyCommandStates();
            }
        }
    }

    public string SelectedTranscodingModeDescription =>
        SelectedTranscodingModeOption.Mode == TranscodingMode.FullTranscode
            ? BuildAccurateTrimModeDescription()
            : SelectedTranscodingModeOption.Description;

    public string OutputDirectory
    {
        get => _outputDirectory;
        set
        {
            var normalized = NormalizeOutputDirectory(value);
            if (SetProperty(ref _outputDirectory, normalized))
            {
                OnPropertyChanged(nameof(HasCustomOutputDirectory));
                RefreshPlannedOutputPath();
                PersistPreferences();
                NotifyCommandStates();
            }
        }
    }

    public bool HasCustomOutputDirectory => !string.IsNullOrWhiteSpace(OutputDirectory);

    public string PlannedOutputPath
    {
        get => _plannedOutputPath;
        private set => SetProperty(ref _plannedOutputPath, value);
    }

    public double TimelineMinimum => HasInput ? _selectionStart.TotalMilliseconds : 0d;

    public double TimelineMaximum => HasInput
        ? Math.Max(TimelineMinimum + 1d, _selectionEnd.TotalMilliseconds)
        : 1d;

    public double SelectionMaximum => Math.Max(1d, _mediaDuration.TotalMilliseconds);

    public double CurrentPositionMilliseconds
    {
        get => _currentPosition.TotalMilliseconds;
        set => SetCurrentPosition(TimeSpan.FromMilliseconds(value));
    }

    public double SelectionStartMilliseconds
    {
        get => _selectionStart.TotalMilliseconds;
        set => SetSelectionStart(TimeSpan.FromMilliseconds(value));
    }

    public double SelectionEndMilliseconds
    {
        get => _selectionEnd.TotalMilliseconds;
        set => SetSelectionEnd(TimeSpan.FromMilliseconds(value));
    }

    public double VolumePercent
    {
        get => _volume * 100d;
        set
        {
            var normalized = Math.Clamp(value, 0d, 100d) / 100d;
            if (SetProperty(ref _volume, normalized))
            {
                UpdatePreviewVolume();
                PersistPreferences();
                OnPropertyChanged(nameof(VolumeLevel));
                OnPropertyChanged(nameof(VolumePercentText));
                OnPropertyChanged(nameof(VolumeToolTipText));
                OnPropertyChanged(nameof(IsMuted));
                OnPropertyChanged(nameof(VolumeButtonSymbol));
                OnPropertyChanged(nameof(VolumeButtonText));
            }
        }
    }

    public double VolumeLevel => _volume;

    public bool IsMuted => VolumePercent <= 0.5d;

    public Symbol VolumeButtonSymbol => IsMuted ? Symbol.Mute : Symbol.Volume;

    public string VolumeButtonText => IsMuted
        ? GetLocalizedText("trim.preview.muted", "\u9759\u97f3")
        : VolumePercentText;

    public string VolumePercentText => $"{Math.Round(VolumePercent):0}%";

    public string VolumeToolTipText => FormatLocalizedText(
        "trim.preview.volumeTooltip",
        "\u97f3\u91cf\uff1a{percent}",
        ("percent", VolumePercentText));

    public string SelectionTimeInputFormatHint => GetSelectionInputFormatPattern();

    public int SelectionTimeInputMaxLength => SelectionTimeInputFormatHint.Length;

    public double SelectionTimeInputBoxWidth => GetSelectionTimeInputBoxWidth();

    public string SelectionTimeInputAssistText => FormatLocalizedText(
        "trim.selection.inputAssist",
        "\u683c\u5f0f\uff1a{pattern}",
        ("pattern", SelectionTimeInputFormatHint));

    public string SelectionStartInputText
    {
        get => _selectionStartInputText;
        set => SetSelectionInputText(SelectionInputKind.Start, value);
    }

    public string SelectionEndInputText
    {
        get => _selectionEndInputText;
        set => SetSelectionInputText(SelectionInputKind.End, value);
    }

    public string CurrentPositionText => FormatSelectionTime(_currentPosition);

    public string TimelinePositionText => $"{FormatCompactSelectionTime(_currentPosition)} / {FormatCompactSelectionTime(_mediaDuration)}";

    public string SelectionStartText => FormatSelectionTime(_selectionStart);

    public string SelectionEndText => FormatSelectionTime(_selectionEnd);

    public string MediaDurationText => FormatSelectionTime(_mediaDuration);

    public TimeSpan SelectedDuration => _selectionEnd > _selectionStart ? _selectionEnd - _selectionStart : TimeSpan.Zero;

    public string SelectedDurationText => FormatSelectionTime(SelectedDuration);

    public string SelectionSummaryText => HasInput
        ? FormatLocalizedText(
            "trim.selection.summary",
            "\u88c1\u526a\u533a\u95f4\uff1a{start} - {end}\uff08\u5171 {duration}\uff09",
            ("start", SelectionStartText),
            ("end", SelectionEndText),
            ("duration", SelectedDurationText))
        : GetLocalizedText(
            "trim.selection.summaryPlaceholder",
            "\u5bfc\u5165\u6587\u4ef6\u540e\u5373\u53ef\u8bbe\u7f6e\u88c1\u526a\u533a\u95f4\u3002");

    public string PlaceholderImportButtonText => GetLocalizedText(
        "trim.placeholder.importButton",
        "\u5bfc\u5165\u6587\u4ef6");

    public string JumpToSelectionStartText => GetLocalizedText(
        "trim.preview.jumpToStart",
        "\u8df3\u8f6c\u5230\u88c1\u526a\u5165\u70b9");

    public string JumpToSelectionEndText => GetLocalizedText(
        "trim.preview.jumpToEnd",
        "\u8df3\u8f6c\u5230\u88c1\u526a\u51fa\u70b9");

    public string TimelineSliderAutomationName => GetLocalizedText(
        "trim.preview.timelineAutomation",
        "\u65f6\u95f4\u8f74\u5b9a\u4f4d");

    public string VolumeTitleText => GetLocalizedText(
        "trim.preview.volume",
        "\u97f3\u91cf");

    public string SelectionRangeSectionTitle => GetLocalizedText(
        "trim.selection.sectionTitle",
        "\u88c1\u526a\u533a\u95f4");

    public string SelectionStartLabelText => GetLocalizedText(
        "trim.selection.startLabel",
        "\u5165\u70b9");

    public string SelectionEndLabelText => GetLocalizedText(
        "trim.selection.endLabel",
        "\u51fa\u70b9");

    public string SelectionDurationLabelText => GetLocalizedText(
        "trim.selection.durationLabel",
        "\u7247\u6bb5\u65f6\u957f");

    public string SelectionStartInputAutomationName => GetLocalizedText(
        "trim.selection.startInputAutomation",
        "\u88c1\u526a\u5165\u70b9\u65f6\u95f4\u8f93\u5165");

    public string SelectionEndInputAutomationName => GetLocalizedText(
        "trim.selection.endInputAutomation",
        "\u88c1\u526a\u51fa\u70b9\u65f6\u95f4\u8f93\u5165");

    public string CurrentSelectionTitleText => GetLocalizedText(
        "trim.selection.currentTitle",
        "\u5f53\u524d\u9009\u533a");

    public string OutputSettingsTitleText => GetLocalizedText(
        "trim.settings.sectionTitle",
        "\u8f93\u51fa\u8bbe\u7f6e");

    public string OutputFormatLabelText => GetLocalizedText(
        "trim.settings.outputFormatLabel",
        "\u8f93\u51fa\u683c\u5f0f");

    public string TrimStrategyLabelText => GetLocalizedText(
        "trim.settings.strategyLabel",
        "\u88c1\u526a\u7b56\u7565");

    public string OutputDirectoryLabelText => GetLocalizedText(
        "trim.settings.outputDirectoryLabel",
        "\u8f93\u51fa\u76ee\u5f55");

    public string SelectOutputDirectoryButtonText => GetLocalizedText(
        "trim.settings.selectDirectory",
        "\u9009\u62e9\u76ee\u5f55");

    public string ClearOutputDirectoryButtonText => GetLocalizedText(
        "trim.settings.clearDirectory",
        "\u6e05\u7a7a");

    public string PlannedOutputPathLabelText => GetLocalizedText(
        "trim.settings.plannedOutputPathLabel",
        "\u8ba1\u5212\u8f93\u51fa\u8def\u5f84");

    public string BasicInfoSectionTitleText => GetLocalizedText(
        "trim.mediaInfo.section.basic",
        "\u57fa\u7840\u4fe1\u606f");

    public string VideoInfoSectionTitleText => GetLocalizedText(
        "trim.mediaInfo.section.video",
        "\u89c6\u9891\u4fe1\u606f");

    public string AudioInfoSectionTitleText => GetLocalizedText(
        "trim.mediaInfo.section.audio",
        "\u97f3\u9891\u4fe1\u606f");

    internal string PreviewUnavailableMessage => IsAudioTrim
        ? GetLocalizedText(
            "trim.preview.failed.audioUnavailable",
            "\u5f53\u524d\u97f3\u9891\u65e0\u6cd5\u9884\u89c8\uff0c\u4f46\u4ecd\u53ef\u5c1d\u8bd5\u76f4\u63a5\u5bfc\u51fa\u3002")
        : GetLocalizedText(
            "trim.preview.failed.videoUnavailable",
            "\u5f53\u524d\u89c6\u9891\u65e0\u6cd5\u9884\u89c8\uff0c\u4f46\u4ecd\u53ef\u5c1d\u8bd5\u76f4\u63a5\u5bfc\u51fa\u3002");

    public Visibility ExportProgressVisibility
    {
        get => _exportProgressVisibility;
        private set => SetProperty(ref _exportProgressVisibility, value);
    }

    public bool IsExportProgressIndeterminate
    {
        get => _isExportProgressIndeterminate;
        private set => SetProperty(ref _isExportProgressIndeterminate, value);
    }

    public double ExportProgressValue
    {
        get => _exportProgressValue;
        private set => SetProperty(ref _exportProgressValue, value);
    }

    public string ExportProgressSummaryText
    {
        get => _exportProgressSummaryText;
        private set => SetProperty(ref _exportProgressSummaryText, value);
    }

    public string ExportProgressDetailText
    {
        get => _exportProgressDetailText;
        private set => SetProperty(ref _exportProgressDetailText, value);
    }

    public string ExportProgressPercentText
    {
        get => _exportProgressPercentText;
        private set => SetProperty(ref _exportProgressPercentText, value);
    }

    public async Task ImportPathsAsync(IEnumerable<string> inputPaths)
    {
        ArgumentNullException.ThrowIfNull(inputPaths);

        if (IsBusy)
        {
            SetStatusMessage(() => GetLocalizedText(
                "trim.status.busy",
                "\u5f53\u524d\u6b63\u5728\u5bfc\u51fa\u7247\u6bb5\uff0c\u8bf7\u7b49\u5f85\u5b8c\u6210\u6216\u5148\u53d6\u6d88\u3002"));
            return;
        }

        if (HasInput)
        {
            SetStatusMessage(() => GetLocalizedText(
                "trim.status.hasInputAlready",
                "\u5f53\u524d\u5df2\u6709\u6587\u4ef6\uff0c\u8bf7\u5148\u79fb\u9664\u540e\u518d\u5bfc\u5165\u65b0\u7684\u6587\u4ef6\u3002"));
            return;
        }

        var paths = inputPaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (paths.Length == 0)
        {
            return;
        }

        SetStatusMessage(() => GetLocalizedText(
            "trim.status.importingMediaInfo",
            "\u6b63\u5728\u89e3\u6790\u5a92\u4f53\u4fe1\u606f..."));
        LastImportErrorDetails = string.Empty;
        SetPreviewPreparingToGeneric();

        var importResult = await _trimWorkflowService.ImportAsync(paths);
        if (importResult.Outcome == VideoTrimImportOutcome.Rejected)
        {
            SetStatusMessage(() => importResult.Message);
            return;
        }

        if (importResult.Outcome == VideoTrimImportOutcome.Failed ||
            importResult.MediaKind is not TrimMediaKind mediaKind ||
            importResult.Snapshot is null ||
            importResult.MediaDuration is not TimeSpan duration)
        {
            ApplyImportFailure(importResult.Message, importResult.DiagnosticDetails);
            return;
        }

        SetCurrentMediaKind(mediaKind);
        _inputPath = importResult.InputPath!;
        _inputFileName = importResult.InputFileName!;
        _mediaDetailsSnapshot = importResult.Snapshot;
        _mediaDuration = duration;
        _selectionStart = TimeSpan.Zero;
        _selectionEnd = duration;
        _currentPosition = TimeSpan.Zero;
        ResetPreviewInteractionState();
        RefreshMediaInfoFields();
        RaiseTrimStateChanged();
        RefreshPlannedOutputPath();
        LastImportErrorDetails = string.Empty;
        SetStatusMessage(() => FormatLocalizedText(
            "trim.status.importSuccess",
            "\u5df2\u5bfc\u5165 {fileName}\uff0c\u8bf7\u62d6\u52a8\u5165\u70b9\u548c\u51fa\u70b9\u786e\u8ba4\u88c1\u526a\u8303\u56f4\u3002",
            ("fileName", _inputFileName)));
    }

    public void ApplyPlayableDuration(TimeSpan duration)
    {
        if (!HasInput || duration <= TimeSpan.Zero || AreClose(_mediaDuration, duration))
        {
            return;
        }

        var keepFullRange = _selectionStart == TimeSpan.Zero && AreClose(_selectionEnd, _mediaDuration);
        _mediaDuration = duration;

        var minimumRange = duration < MinimumSelectionLength ? duration : MinimumSelectionLength;
        if (keepFullRange || _selectionEnd > duration)
        {
            _selectionEnd = duration;
        }

        var maxStart = MaxTime(TimeSpan.Zero, _selectionEnd - minimumRange);
        if (_selectionStart > maxStart)
        {
            _selectionStart = maxStart;
        }

        _currentPosition = ClampToSelection(_currentPosition);
        RefreshMediaInfoFields();
        RaiseTimelineChanged();
    }

    public void SetPreviewPreparing(string message)
    {
        ResetPreviewInteractionState();
        IsPreviewReady = false;
        SetPreviewStateMessage(() => string.IsNullOrWhiteSpace(message)
            ? GetDefaultPreviewPreparingMessage()
            : message);
    }

    public void SetPreviewReady()
    {
        IsPreviewReady = true;
        SetPreviewStateMessage(() => string.Empty);
        OnPropertyChanged(nameof(PreviewOverlayMessage));
    }

    public void SetPreviewFailed(string message)
    {
        ResetPreviewInteractionState();
        IsPreviewReady = false;
        SetPreviewStateMessage(() => string.IsNullOrWhiteSpace(message)
            ? GetLocalizedText(
                "trim.preview.failed.fileUnavailable",
                "\u5f53\u524d\u6587\u4ef6\u6682\u65f6\u65e0\u6cd5\u9884\u89c8\u3002")
            : message);
    }

    internal void SetPreviewPreparingToDefault() => SetPreviewPreparing(GetDefaultPreviewPreparingMessage());

    internal void SetPreviewPreparingToGeneric() => SetPreviewPreparing(GetLocalizedText(
        "trim.preview.preparing.generic",
        "\u6b63\u5728\u51c6\u5907\u9884\u89c8..."));

    internal void SetPreviewUnavailable() => SetPreviewFailed(PreviewUnavailableMessage);

    public void SetPlaying(bool isPlaying) => IsPlaying = isPlaying && CanPlayPreview;

    public void SyncCurrentPosition(TimeSpan position) => SetCurrentPosition(position);

    public void BeginSelectionStartInputEdit() => _isSelectionStartInputEditing = true;

    public void BeginSelectionEndInputEdit() => _isSelectionEndInputEditing = true;

    public void CommitSelectionStartInput()
    {
        CommitSelectionInput(SelectionInputKind.Start);
    }

    public void CommitSelectionEndInput()
    {
        CommitSelectionInput(SelectionInputKind.End);
    }

    public bool IsPotentialSelectionInputText(string? text)
    {
        if (text is null)
        {
            return true;
        }

        var candidate = text.Trim();
        if (candidate.Length == 0)
        {
            return true;
        }

        if (candidate.Length > SelectionTimeInputMaxLength ||
            candidate.Any(character => !char.IsDigit(character) && character is not ':' and not '.'))
        {
            return false;
        }

        return GetSelectionInputFormat() switch
        {
            SelectionInputFormat.Hours => IsPotentialCompositeTimeInput(candidate, expectedSegmentCount: 3),
            SelectionInputFormat.Minutes => IsPotentialCompositeTimeInput(candidate, expectedSegmentCount: 2),
            _ => IsPotentialSecondsOnlyInput(candidate)
        };
    }

    public TimeSpan EnsureCurrentPositionWithinSelection()
    {
        SetCurrentPosition(ClampToSelection(_currentPosition));
        return _currentPosition;
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _exportCancellationSource?.Cancel();
        _exportCancellationSource?.Dispose();
        _exportCancellationSource = null;
        DisposePreview();
    }

    private async Task SelectVideoAsync()
    {
        try
        {
            var file = await _filePickerService.PickSingleFileAsync(
                new FilePickerRequest(
                    _configuration.SupportedTrimInputFileTypes,
                    GetLocalizedText("trim.picker.importFileTitle", "\u5bfc\u5165\u6587\u4ef6")));

            if (string.IsNullOrWhiteSpace(file))
            {
                SetStatusMessage(() => GetLocalizedText(
                    "trim.status.importCancelled",
                    "\u5df2\u53d6\u6d88\u5bfc\u5165\u6587\u4ef6\u3002"));
                return;
            }

            await ImportPathsAsync(new[] { file });
        }
        catch (OperationCanceledException)
        {
            SetStatusMessage(() => GetLocalizedText(
                "trim.status.importCancelled",
                "\u5df2\u53d6\u6d88\u5bfc\u5165\u6587\u4ef6\u3002"));
        }
        catch (Exception exception)
        {
            ApplyImportFailure(
                ExtractFriendlyExceptionMessage(exception),
                BuildExceptionDiagnosticDetails(exception));
            _logger.Log(LogLevel.Error, "\u88c1\u526a\u6a21\u5757\u9009\u62e9\u6587\u4ef6\u65f6\u53d1\u751f\u5f02\u5e38\u3002", exception);
        }
    }

    private void RemoveVideo()
    {
        if (!HasInput || IsBusy)
        {
            return;
        }

        _inputPath = string.Empty;
        _inputFileName = string.Empty;
        _mediaDetailsSnapshot = null;
        _plannedOutputPath = string.Empty;
        _mediaDuration = TimeSpan.Zero;
        _selectionStart = TimeSpan.Zero;
        _selectionEnd = TimeSpan.Zero;
        _currentPosition = TimeSpan.Zero;
        ResetPreviewInteractionState();
        LastImportErrorDetails = string.Empty;
        SetPreviewStateMessage(() => GetLocalizedText(
            "trim.status.ready",
            "\u8bf7\u5bfc\u5165\u97f3\u9891\u6216\u89c6\u9891\u6587\u4ef6\uff0c\u4e5f\u53ef\u62d6\u62fd\u5230\u6b64\u5904\u5f00\u59cb\u88c1\u526a\u3002"));
        IsPreviewReady = false;
        OnPropertyChanged(nameof(PreviewOverlayMessage));
        RefreshMediaInfoFields();
        RaiseTrimStateChanged();
        SetStatusMessage(() => GetLocalizedText(
            "trim.status.removedInput",
            "\u5df2\u79fb\u9664\u5f53\u524d\u6587\u4ef6\u3002"));
    }

    private async Task SelectOutputDirectoryAsync()
    {
        try
        {
            var folder = await _filePickerService.PickFolderAsync(
                GetLocalizedText("trim.picker.outputDirectoryTitle", "\u9009\u62e9\u88c1\u526a\u8f93\u51fa\u76ee\u5f55"));
            if (string.IsNullOrWhiteSpace(folder))
            {
                SetStatusMessage(() => GetLocalizedText(
                    "trim.status.outputDirectoryCancelled",
                    "\u5df2\u53d6\u6d88\u9009\u62e9\u8f93\u51fa\u76ee\u5f55\u3002"));
                return;
            }

            OutputDirectory = folder;
            SetStatusMessage(() => FormatLocalizedText(
                "trim.status.outputDirectoryUpdated",
                "\u5df2\u5c06\u88c1\u526a\u8f93\u51fa\u76ee\u5f55\u8bbe\u7f6e\u4e3a\uff1a{path}",
                ("path", OutputDirectory)));
        }
        catch (OperationCanceledException)
        {
            SetStatusMessage(() => GetLocalizedText(
                "trim.status.outputDirectoryCancelled",
                "\u5df2\u53d6\u6d88\u9009\u62e9\u8f93\u51fa\u76ee\u5f55\u3002"));
        }
        catch (Exception exception)
        {
            SetStatusMessage(() => GetLocalizedText(
                "trim.status.outputDirectoryFailed",
                "\u9009\u62e9\u8f93\u51fa\u76ee\u5f55\u5931\u8d25\u3002"));
            _logger.Log(LogLevel.Error, "\u88c1\u526a\u6a21\u5757\u9009\u62e9\u8f93\u51fa\u76ee\u5f55\u65f6\u53d1\u751f\u5f02\u5e38\u3002", exception);
        }
    }

    private void ClearOutputDirectory()
    {
        if (!HasCustomOutputDirectory || IsBusy)
        {
            return;
        }

        OutputDirectory = string.Empty;
        SetStatusMessage(() => GetLocalizedText(
            "trim.status.outputDirectoryCleared",
            "\u5df2\u6e05\u7a7a\u88c1\u526a\u8f93\u51fa\u76ee\u5f55\uff0c\u5bfc\u51fa\u65f6\u5c06\u4f7f\u7528\u539f\u6587\u4ef6\u6240\u5728\u6587\u4ef6\u5939\u3002"));
    }

    private async Task ExportTrimAsync()
    {
        if (!CanExportTrim())
        {
            SetStatusMessage(() => HasInput
                ? GetLocalizedText(
                    "trim.status.invalidSelection",
                    "\u5f53\u524d\u88c1\u526a\u533a\u95f4\u65e0\u6548\uff0c\u8bf7\u91cd\u65b0\u8c03\u6574\u5165\u70b9\u548c\u51fa\u70b9\u3002")
                : GetLocalizedText(
                    "trim.status.needsInput",
                    "\u8bf7\u5148\u5bfc\u5165\u4e00\u4e2a\u6587\u4ef6\u3002"));
            return;
        }

        _exportCancellationSource?.Dispose();
        _exportCancellationSource = new CancellationTokenSource();

        try
        {
            IsBusy = true;
            IsPlaying = false;
            EnsureOutputDirectoryExists();
            RefreshPlannedOutputPath();
            ShowExportPreparationProgress();
            SetStatusMessage(() => GetLocalizedText(
                "trim.status.exportPreparing",
                "\u6b63\u5728\u51c6\u5907\u5bfc\u51fa\u88c1\u526a\u7247\u6bb5..."));

            var preferences = _userPreferencesService.Load();
            var request = new VideoTrimExportRequest(
                _inputPath,
                PlannedOutputPath,
                _selectionStart,
                _selectionEnd,
                _currentMediaKind,
                SelectedOutputFormat,
                SelectedTranscodingModeOption.Mode,
                VideoAccelerationKind.None);
            var exportResult = await _trimWorkflowService.ExportAsync(
                request,
                preferences,
                new Progress<FFmpegProgressUpdate>(UpdateExportProgress),
                () => SetStatusMessage(() => GetLocalizedText(
                    "trim.status.gpuFallbackRetry",
                    "GPU \u7f16\u7801\u5931\u8d25\uff0c\u5df2\u81ea\u52a8\u56de\u9000\u4e3a CPU \u91cd\u8bd5\u4e00\u6b21\u3002")),
                _exportCancellationSource.Token);
            var result = exportResult.ExecutionResult;
            if (result.WasSuccessful && File.Exists(exportResult.Request.OutputPath))
            {
                ShowExportFinishingProgress();
                var outputFileName = Path.GetFileName(exportResult.Request.OutputPath);
                SetStatusMessage(() => AppendStatusSuffix(
                    FormatLocalizedText(
                        "trim.status.exportCompleted",
                        "\u88c1\u526a\u5bfc\u51fa\u5b8c\u6210\uff1a{fileName}",
                        ("fileName", outputFileName)),
                    exportResult.TranscodingMessage));
                TryRevealOutputFile(exportResult.Request.OutputPath);
                return;
            }

            var failureReason = ExtractFriendlyFailureMessage(result);
            SetStatusMessage(() => result.WasCancelled
                ? GetLocalizedText(
                    "trim.status.exportCancelled",
                    "\u5df2\u53d6\u6d88\u88c1\u526a\u5bfc\u51fa\u3002")
                : AppendStatusSuffix(
                    FormatLocalizedText(
                        "trim.status.exportFailed",
                        "\u88c1\u526a\u5bfc\u51fa\u5931\u8d25\uff1a{reason}",
                        ("reason", failureReason)),
                    exportResult.TranscodingMessage));
        }
        catch (OperationCanceledException) when (_exportCancellationSource?.IsCancellationRequested == true)
        {
            SetStatusMessage(() => GetLocalizedText(
                "trim.status.exportCancelled",
                "\u5df2\u53d6\u6d88\u88c1\u526a\u5bfc\u51fa\u3002"));
        }
        catch (Exception exception)
        {
            SetStatusMessage(() => GetLocalizedText(
                "trim.status.exportException",
                "\u88c1\u526a\u5bfc\u51fa\u8fc7\u7a0b\u4e2d\u53d1\u751f\u5f02\u5e38\u3002"));
            _logger.Log(LogLevel.Error, "\u88c1\u526a\u6a21\u5757\u5bfc\u51fa\u7247\u6bb5\u65f6\u53d1\u751f\u5f02\u5e38\u3002", exception);
        }
        finally
        {
            ResetExportProgress();
            IsBusy = false;
            _exportCancellationSource?.Dispose();
            _exportCancellationSource = null;
        }
    }

    private void CancelExport() => _exportCancellationSource?.Cancel();

    private bool CanExportTrim() =>
        !IsBusy &&
        HasInput &&
        SelectedDuration > TimeSpan.Zero &&
        !string.IsNullOrWhiteSpace(PlannedOutputPath);

    private void RefreshMediaInfoFields()
    {
        var snapshot = _mediaDetailsSnapshot;
        _basicInfoFields = snapshot is null
            ? Array.Empty<MediaDetailField>()
            : BuildBasicInfoFields(snapshot);
        _videoInfoFields = snapshot is null || !snapshot.HasVideoStream
            ? Array.Empty<MediaDetailField>()
            : BuildVideoInfoFields(snapshot);
        _audioInfoFields = snapshot is null || !snapshot.HasAudioStream
            ? Array.Empty<MediaDetailField>()
            : BuildAudioInfoFields(snapshot);

        OnPropertyChanged(nameof(BasicInfoFields));
        OnPropertyChanged(nameof(VideoInfoFields));
        OnPropertyChanged(nameof(AudioInfoFields));
        OnPropertyChanged(nameof(BasicInfoVisibility));
        OnPropertyChanged(nameof(VideoInfoVisibility));
        OnPropertyChanged(nameof(AudioInfoVisibility));
    }

    private IReadOnlyList<MediaDetailField> BuildBasicInfoFields(MediaDetailsSnapshot snapshot) =>
        new[]
        {
            new MediaDetailField
            {
                Label = GetLocalizedText("trim.mediaInfo.field.size", "\u5927\u5c0f"),
                Value = FormatFileSize(GetInputFileSizeBytes(snapshot.InputPath))
            },
            new MediaDetailField
            {
                Label = GetLocalizedText("trim.mediaInfo.field.duration", "\u65f6\u957f"),
                Value = FormatInfoDuration(_mediaDuration)
            }
        };

    private IReadOnlyList<MediaDetailField> BuildVideoInfoFields(MediaDetailsSnapshot snapshot) =>
        new[]
        {
            new MediaDetailField
            {
                Label = GetLocalizedText("trim.mediaInfo.field.resolution", "\u5206\u8fa8\u7387"),
                Value = GetFieldValue(snapshot.VideoFields, "\u5206\u8fa8\u7387")
            },
            new MediaDetailField
            {
                Label = GetLocalizedText("trim.mediaInfo.field.frameRate", "\u5e27\u7387"),
                Value = GetFieldValue(snapshot.VideoFields, "\u5e27\u7387")
            },
            new MediaDetailField
            {
                Label = GetLocalizedText("trim.mediaInfo.field.codec", "\u7f16\u7801"),
                Value = GetFieldValue(snapshot.VideoFields, "\u7f16\u7801")
            }
        };

    private IReadOnlyList<MediaDetailField> BuildAudioInfoFields(MediaDetailsSnapshot snapshot) =>
        new[]
        {
            new MediaDetailField
            {
                Label = GetLocalizedText("trim.mediaInfo.field.codec", "\u7f16\u7801"),
                Value = GetFieldValue(snapshot.AudioFields, "\u7f16\u7801")
            },
            new MediaDetailField
            {
                Label = GetLocalizedText("trim.mediaInfo.field.sampleRate", "\u91c7\u6837\u7387"),
                Value = GetFieldValue(snapshot.AudioFields, "\u91c7\u6837\u7387")
            },
            new MediaDetailField
            {
                Label = GetLocalizedText("trim.mediaInfo.field.channels", "\u58f0\u9053"),
                Value = GetFieldValue(snapshot.AudioFields, "\u58f0\u9053")
            }
        };

    private string GetFieldValue(IEnumerable<MediaDetailField> fields, string label)
    {
        var value = fields.FirstOrDefault(field => string.Equals(field.Label, label, StringComparison.Ordinal))?.Value;
        return string.IsNullOrWhiteSpace(value)
            ? GetLocalizedText("trim.mediaInfo.unknown", "\u672a\u77e5")
            : value;
    }

    private static long GetInputFileSizeBytes(string inputPath)
    {
        try
        {
            return File.Exists(inputPath) ? new FileInfo(inputPath).Length : 0L;
        }
        catch (IOException)
        {
            return 0L;
        }
        catch (UnauthorizedAccessException)
        {
            return 0L;
        }
    }

    private void NotifyCommandStates()
    {
        _selectVideoCommand.NotifyCanExecuteChanged();
        _removeVideoCommand.NotifyCanExecuteChanged();
        _selectOutputDirectoryCommand.NotifyCanExecuteChanged();
        _clearOutputDirectoryCommand.NotifyCanExecuteChanged();
        _exportTrimCommand.NotifyCanExecuteChanged();
        _cancelExportCommand.NotifyCanExecuteChanged();
    }

    private void RaiseTrimStateChanged()
    {
        OnPropertyChanged(nameof(HasInput));
        OnPropertyChanged(nameof(IsAudioTrim));
        OnPropertyChanged(nameof(IsVideoTrim));
        OnPropertyChanged(nameof(PlaceholderVisibility));
        OnPropertyChanged(nameof(EditorVisibility));
        OnPropertyChanged(nameof(CanEditSelectionInputs));
        OnPropertyChanged(nameof(CanPlayPreview));
        OnPropertyChanged(nameof(CanJumpToSelectionBoundary));
        OnPropertyChanged(nameof(InputPath));
        OnPropertyChanged(nameof(InputFileName));
        OnPropertyChanged(nameof(EditorTitle));
        OnPropertyChanged(nameof(EditorDescription));
        OnPropertyChanged(nameof(CurrentFileCaptionText));
        OnPropertyChanged(nameof(PlaceholderIconSymbol));
        OnPropertyChanged(nameof(PlaceholderTitleText));
        OnPropertyChanged(nameof(PlaceholderDescriptionText));
        OnPropertyChanged(nameof(PlaceholderImportButtonText));
        OnPropertyChanged(nameof(ImportErrorDetailsTitle));
        OnPropertyChanged(nameof(SelectionRuleDescription));
        OnPropertyChanged(nameof(OutputDirectoryHintText));
        OnPropertyChanged(nameof(MediaInfoPanelTitle));
        OnPropertyChanged(nameof(RemoveInputCommandText));
        OnPropertyChanged(nameof(JumpToSelectionStartText));
        OnPropertyChanged(nameof(JumpToSelectionEndText));
        OnPropertyChanged(nameof(TimelineSliderAutomationName));
        OnPropertyChanged(nameof(VolumeTitleText));
        OnPropertyChanged(nameof(SelectionRangeSectionTitle));
        OnPropertyChanged(nameof(SelectionStartLabelText));
        OnPropertyChanged(nameof(SelectionEndLabelText));
        OnPropertyChanged(nameof(SelectionDurationLabelText));
        OnPropertyChanged(nameof(SelectionStartInputAutomationName));
        OnPropertyChanged(nameof(SelectionEndInputAutomationName));
        OnPropertyChanged(nameof(CurrentSelectionTitleText));
        OnPropertyChanged(nameof(OutputSettingsTitleText));
        OnPropertyChanged(nameof(OutputFormatLabelText));
        OnPropertyChanged(nameof(TrimStrategyLabelText));
        OnPropertyChanged(nameof(OutputDirectoryLabelText));
        OnPropertyChanged(nameof(SelectOutputDirectoryButtonText));
        OnPropertyChanged(nameof(ClearOutputDirectoryButtonText));
        OnPropertyChanged(nameof(PlannedOutputPathLabelText));
        OnPropertyChanged(nameof(BasicInfoSectionTitleText));
        OnPropertyChanged(nameof(VideoInfoSectionTitleText));
        OnPropertyChanged(nameof(AudioInfoSectionTitleText));
        OnPropertyChanged(nameof(PreviewOverlayVisibility));
        OnPropertyChanged(nameof(PreviewOverlayMessage));
        OnPropertyChanged(nameof(PreviewOverlaySymbol));
        OnPropertyChanged(nameof(AudioPreviewSurfaceVisibility));
        OnPropertyChanged(nameof(AudioPreviewTitleText));
        OnPropertyChanged(nameof(AudioPreviewSubtitleText));
        OnPropertyChanged(nameof(AudioPreviewFormatBadgeText));
        OnPropertyChanged(nameof(VolumeToolTipText));
        OnPropertyChanged(nameof(VolumeButtonText));
        RaiseTimelineChanged();
        NotifyCommandStates();
    }

    private void RaiseTimelineChanged()
    {
        OnPropertyChanged(nameof(TimelineMinimum));
        OnPropertyChanged(nameof(TimelineMaximum));
        OnPropertyChanged(nameof(SelectionMaximum));
        OnPropertyChanged(nameof(SelectionTimeInputFormatHint));
        OnPropertyChanged(nameof(SelectionTimeInputMaxLength));
        OnPropertyChanged(nameof(SelectionTimeInputBoxWidth));
        OnPropertyChanged(nameof(SelectionTimeInputAssistText));
        OnPropertyChanged(nameof(CurrentPositionMilliseconds));
        OnPropertyChanged(nameof(CurrentPositionText));
        OnPropertyChanged(nameof(TimelinePositionText));
        OnPropertyChanged(nameof(SelectionStartMilliseconds));
        OnPropertyChanged(nameof(SelectionEndMilliseconds));
        OnPropertyChanged(nameof(SelectionStartText));
        OnPropertyChanged(nameof(SelectionEndText));
        OnPropertyChanged(nameof(MediaDurationText));
        OnPropertyChanged(nameof(SelectedDuration));
        OnPropertyChanged(nameof(SelectedDurationText));
        OnPropertyChanged(nameof(SelectionSummaryText));
        RefreshSelectionInputTexts();
        NotifyCommandStates();
    }

    private void RefreshPlannedOutputPath()
    {
        PlannedOutputPath = HasInput
            ? MediaPathResolver.CreateUniqueOutputPath(
                MediaPathResolver.CreateTrimOutputPath(
                    _inputPath,
                    SelectedOutputFormat.Extension,
                    HasCustomOutputDirectory ? OutputDirectory : null))
            : string.Empty;
    }

    private void PersistPreferences()
    {
        _userPreferencesService.Update(existing => existing with
        {
            PreferredTrimOutputFormatExtension = _selectedOutputFormat?.Extension,
            PreferredTrimOutputDirectory = HasCustomOutputDirectory ? OutputDirectory : null,
            PreferredTrimTranscodingMode = SelectedTranscodingModeOption.Mode,
            PreferredTrimPreviewVolumePercent = VolumePercent
        });
    }

    private static double ResolvePreferredVolumePercent(double volumePercent) =>
        double.IsNaN(volumePercent) || double.IsInfinity(volumePercent)
            ? DefaultTrimPreviewVolumePercent
            : Math.Clamp(volumePercent, 0d, 100d);

    private OutputFormatOption ResolvePreferredOutputFormat(string? extension) =>
        _availableOutputFormats.FirstOrDefault(item => string.Equals(item.Extension, extension, StringComparison.OrdinalIgnoreCase))
        ?? _availableOutputFormats.First();

    private void RefreshLocalizedRuntimeText()
    {
        if (_statusMessageResolver is not null)
        {
            StatusMessage = _statusMessageResolver.Invoke();
        }

        if (_previewStateMessageResolver is not null)
        {
            PreviewStateMessage = _previewStateMessageResolver.Invoke();
            OnPropertyChanged(nameof(PreviewOverlayMessage));
        }

        _exportProgressStateApplier?.Invoke();
    }

    private IReadOnlyList<TranscodingModeOption> BuildTranscodingOptions() =>
        new[]
        {
            new TranscodingModeOption(
                TranscodingMode.FastContainerConversion,
                GetLocalizedText(
                    "trim.strategy.option.fast.displayName",
                    "\u901f\u5ea6\u4f18\u5148"),
                GetLocalizedText(
                    "trim.strategy.option.fast.description",
                    "\u4fdd\u7559\u5f53\u524d\u5feb\u901f\u5bfc\u51fa\u8def\u5f84\uff1a\u5f53\u8f93\u5165\u4e0e\u8f93\u51fa\u5bb9\u5668\u517c\u5bb9\u65f6\u4f18\u5148\u76f4\u63a5\u590d\u7528\u539f\u59cb\u6d41\uff0c\u6574\u4f53\u901f\u5ea6\u66f4\u5feb\u3002")),
            new TranscodingModeOption(
                TranscodingMode.FullTranscode,
                GetLocalizedText(
                    "trim.strategy.option.accurate.displayName",
                    "\u7cbe\u786e\u5ea6\u4f18\u5148"),
                GetLocalizedText(
                    "trim.strategy.option.accurate.description",
                    "\u7edf\u4e00\u4f7f\u7528\u7cbe\u786e\u88c1\u526a\u8def\u5f84\uff1b\u89c6\u9891\u4f1a\u4f18\u5148\u5c1d\u8bd5\u667a\u80fd\u88c1\u526a\uff0c\u5fc5\u8981\u65f6\u56de\u9000\u4e3a\u6574\u6bb5\u7cbe\u786e\u91cd\u7f16\u7801\u3002"))
        };

    private TranscodingModeOption ResolvePreferredTranscodingMode(TranscodingMode mode)
    {
        var matchedOption = _transcodingOptions.FirstOrDefault(item => item.Mode == mode);
        return matchedOption ?? _transcodingOptions[0];
    }

    private string BuildAccurateTrimModeDescription()
    {
        var gpuAccelerationEnabled = _userPreferencesService.Load().EnableGpuAccelerationForTranscoding;
        return gpuAccelerationEnabled
            ? GetLocalizedText(
                "trim.strategy.option.accurate.gpuDescription",
                "\u7edf\u4e00\u4f7f\u7528\u8f93\u5165\u540e\u7684 `-ss/-t` \u7cbe\u786e\u88c1\u526a\uff1b\u89c6\u9891\u4f1a\u4f18\u5148\u5c1d\u8bd5 smart trim\uff0c\u5e76\u6309 NVIDIA NVENC -> AMD AMF -> Intel Quick Sync -> CPU \u7684\u987a\u5e8f\u9009\u62e9\u7f16\u7801\u80fd\u529b\u3002")
            : GetLocalizedText(
                "trim.strategy.option.accurate.cpuDescription",
                "\u7edf\u4e00\u4f7f\u7528\u8f93\u5165\u540e\u7684 `-ss/-t` \u7cbe\u786e\u88c1\u526a\uff1b\u89c6\u9891\u4f1a\u4f18\u5148\u5c1d\u8bd5 smart trim\uff0c\u5f53\u524d\u82e5\u9700\u8981\u91cd\u7f16\u7801\u5219\u7531 CPU \u5168\u7a0b\u514c\u5e95\u3002");
    }

    private void ApplyImportFailure(string message, string? diagnosticDetails)
    {
        var friendlyMessage = string.IsNullOrWhiteSpace(message)
            ? "\u65e0\u6cd5\u89e3\u6790\u5f53\u524d\u6587\u4ef6\u3002"
            : message.Trim();
        var diagnosticText = string.IsNullOrWhiteSpace(diagnosticDetails)
            ? string.Empty
            : diagnosticDetails.Trim();

        SetStatusMessage(() => FormatLocalizedText(
            "trim.status.importFailed",
            "\u5bfc\u5165\u6587\u4ef6\u5931\u8d25\uff1a{reason}",
            ("reason", friendlyMessage)));
        LastImportErrorDetails = diagnosticText;
        SetPreviewFailed(string.IsNullOrWhiteSpace(diagnosticText)
            ? GetLocalizedText(
                "trim.preview.failed.checkFile",
                "\u5f53\u524d\u6587\u4ef6\u65e0\u6cd5\u9884\u89c8\uff0c\u8bf7\u68c0\u67e5\u6587\u4ef6\u540e\u91cd\u8bd5\u3002")
            : GetLocalizedText(
                "trim.preview.failed.importDetails",
                "\u5f53\u524d\u6587\u4ef6\u5bfc\u5165\u5931\u8d25\uff0c\u8bf7\u68c0\u67e5\u4e0b\u65b9\u8be6\u7ec6\u9519\u8bef\u4fe1\u606f\u3002"));

        if (!string.IsNullOrWhiteSpace(diagnosticText))
        {
            _logger.Log(LogLevel.Warning, $"\u88c1\u526a\u6a21\u5757\u5bfc\u5165\u6587\u4ef6\u5931\u8d25\u8be6\u60c5\uff1a{Environment.NewLine}{diagnosticText}");
        }
    }

    private void SetStatusMessage(Func<string> resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);

        _statusMessageResolver = resolver;
        StatusMessage = resolver();
    }

    private void SetPreviewStateMessage(Func<string> resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);

        _previewStateMessageResolver = resolver;
        PreviewStateMessage = resolver();
        OnPropertyChanged(nameof(PreviewOverlayMessage));
    }

    private string GetLocalizedText(string key, string fallback) =>
        _localizationService.GetString(key, fallback);

    private string FormatLocalizedText(string key, string fallback, params (string Name, object? Value)[] arguments)
    {
        Dictionary<string, object?>? localizedArguments = null;
        if (arguments.Length > 0)
        {
            localizedArguments = new Dictionary<string, object?>(arguments.Length, StringComparer.Ordinal);
            foreach (var argument in arguments)
            {
                localizedArguments[argument.Name] = argument.Value;
            }
        }

        return _localizationService.Format(key, localizedArguments, fallback);
    }

    private static string AppendStatusSuffix(string message, string? suffix) =>
        string.IsNullOrWhiteSpace(suffix)
            ? message
            : $"{message} {suffix}";

    private string ExtractFriendlyExceptionMessage(Exception exception) =>
        string.IsNullOrWhiteSpace(exception.Message)
            ? GetLocalizedText(
                "trim.import.failure.unknownException",
                "\u5bfc\u5165\u6587\u4ef6\u8fc7\u7a0b\u4e2d\u53d1\u751f\u672a\u77e5\u5f02\u5e38\u3002")
            : exception.Message.Trim();

    private string BuildExceptionDiagnosticDetails(Exception exception)
    {
        var details = exception.ToString().Trim();
        return string.IsNullOrWhiteSpace(details)
            ? GetLocalizedText(
                "trim.import.failure.noDiagnosticDetails",
                "\u672a\u80fd\u6355\u83b7\u5230\u989d\u5916\u7684\u5f02\u5e38\u8bca\u65ad\u4fe1\u606f\u3002")
            : details;
    }

    private void SetCurrentPosition(TimeSpan position)
    {
        var normalized = ClampToSelection(position);
        if (SetProperty(ref _currentPosition, normalized))
        {
            OnPropertyChanged(nameof(CurrentPositionMilliseconds));
            OnPropertyChanged(nameof(CurrentPositionText));
            OnPropertyChanged(nameof(TimelinePositionText));
        }
    }

    private void SetSelectionInputText(SelectionInputKind kind, string? value)
    {
        var normalized = value ?? string.Empty;
        var propertyChanged = kind switch
        {
            SelectionInputKind.Start => SetProperty(ref _selectionStartInputText, normalized, nameof(SelectionStartInputText)),
            _ => SetProperty(ref _selectionEndInputText, normalized, nameof(SelectionEndInputText))
        };

        if (!propertyChanged)
        {
            return;
        }

        TryApplySelectionInput(kind, normalized, normalizeSuccessfulText: false);
    }

    private void CommitSelectionInput(SelectionInputKind kind)
    {
        TryApplySelectionInput(kind, GetSelectionInputText(kind), normalizeSuccessfulText: true);
        SetSelectionInputEditing(kind, false);
        RefreshSelectionInputTexts(force: true);
    }

    private bool TryApplySelectionInput(
        SelectionInputKind kind,
        string? text,
        bool normalizeSuccessfulText)
    {
        if (!HasInput || string.IsNullOrWhiteSpace(text) || !TryParseSelectionInput(text, out var parsed))
        {
            return false;
        }

        if (kind == SelectionInputKind.Start)
        {
            SetSelectionStart(parsed);
        }
        else
        {
            SetSelectionEnd(parsed);
        }

        var effectiveValue = kind == SelectionInputKind.Start ? _selectionStart : _selectionEnd;
        if (normalizeSuccessfulText || !AreClose(parsed, effectiveValue))
        {
            SetSelectionInputTextSilently(kind, FormatSelectionTime(effectiveValue));
        }

        return true;
    }

    private void SetSelectionStart(TimeSpan position)
    {
        if (_mediaDuration <= TimeSpan.Zero)
        {
            return;
        }

        var maxStart = MaxTime(TimeSpan.Zero, _selectionEnd - GetMinimumRange());
        var normalized = Clamp(position, TimeSpan.Zero, maxStart);
        if (SetProperty(ref _selectionStart, normalized))
        {
            if (_selectionEnd - _selectionStart < GetMinimumRange())
            {
                _selectionEnd = MinTime(_mediaDuration, _selectionStart + GetMinimumRange());
            }

            _currentPosition = ClampToSelection(_currentPosition);
            RaiseTimelineChanged();
            QueueSelectionBoundaryWarmup();
        }
    }

    private void SetSelectionEnd(TimeSpan position)
    {
        if (_mediaDuration <= TimeSpan.Zero)
        {
            return;
        }

        var normalized = Clamp(position, _selectionStart + GetMinimumRange(), _mediaDuration);
        if (SetProperty(ref _selectionEnd, normalized))
        {
            _currentPosition = ClampToSelection(_currentPosition);
            RaiseTimelineChanged();
            QueueSelectionBoundaryWarmup();
        }
    }

    private TimeSpan ClampToSelection(TimeSpan position)
    {
        if (_mediaDuration <= TimeSpan.Zero)
        {
            return TimeSpan.Zero;
        }

        var minimum = Clamp(_selectionStart, TimeSpan.Zero, _mediaDuration);
        var maximum = Clamp(_selectionEnd, minimum, _mediaDuration);
        return Clamp(position, minimum, maximum);
    }

    private TimeSpan GetMinimumRange() => _mediaDuration < MinimumSelectionLength ? _mediaDuration : MinimumSelectionLength;

    private void RefreshSelectionInputTexts(bool force = false)
    {
        var startText = HasInput ? FormatSelectionTime(_selectionStart) : string.Empty;
        var endText = HasInput ? FormatSelectionTime(_selectionEnd) : string.Empty;

        if (force || !_isSelectionStartInputEditing)
        {
            SetSelectionInputTextSilently(SelectionInputKind.Start, startText);
        }

        if (force || !_isSelectionEndInputEditing)
        {
            SetSelectionInputTextSilently(SelectionInputKind.End, endText);
        }
    }

    private string GetSelectionInputText(SelectionInputKind kind) =>
        kind == SelectionInputKind.Start ? _selectionStartInputText : _selectionEndInputText;

    private void SetSelectionInputEditing(SelectionInputKind kind, bool isEditing)
    {
        if (kind == SelectionInputKind.Start)
        {
            _isSelectionStartInputEditing = isEditing;
        }
        else
        {
            _isSelectionEndInputEditing = isEditing;
        }
    }

    private void SetSelectionInputTextSilently(SelectionInputKind kind, string text)
    {
        if (kind == SelectionInputKind.Start)
        {
            if (string.Equals(_selectionStartInputText, text, StringComparison.Ordinal))
            {
                return;
            }

            _selectionStartInputText = text;
            OnPropertyChanged(nameof(SelectionStartInputText));
            return;
        }

        if (string.Equals(_selectionEndInputText, text, StringComparison.Ordinal))
        {
            return;
        }

        _selectionEndInputText = text;
        OnPropertyChanged(nameof(SelectionEndInputText));
    }

    private SelectionInputFormat GetSelectionInputFormat()
    {
        if (_mediaDuration >= TimeSpan.FromHours(1))
        {
            return SelectionInputFormat.Hours;
        }

        return _mediaDuration >= TimeSpan.FromMinutes(1)
            ? SelectionInputFormat.Minutes
            : SelectionInputFormat.Seconds;
    }

    private string GetSelectionInputFormatPattern() =>
        GetSelectionInputFormat() switch
        {
            SelectionInputFormat.Hours => "HH:mm:ss.fff",
            SelectionInputFormat.Minutes => "mm:ss.fff",
            _ => "ss.fff"
        };

    private double GetSelectionTimeInputBoxWidth() =>
        GetSelectionInputFormat() switch
        {
            SelectionInputFormat.Hours => 188d,
            SelectionInputFormat.Minutes => 156d,
            _ => 136d
        };

    private bool TryParseSelectionInput(string? text, out TimeSpan value)
    {
        value = TimeSpan.Zero;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var candidate = text.Trim();
        return GetSelectionInputFormat() switch
        {
            SelectionInputFormat.Hours => TryParseCompositeTime(candidate, expectedSegmentCount: 3, out value),
            SelectionInputFormat.Minutes => TryParseCompositeTime(candidate, expectedSegmentCount: 2, out value),
            _ => TryParseSecondsOnlyTime(candidate, out value)
        };
    }

    private static bool TryParseCompositeTime(
        string text,
        int expectedSegmentCount,
        out TimeSpan value)
    {
        value = TimeSpan.Zero;
        if (!TrySplitMilliseconds(text, out var timePart, out var milliseconds))
        {
            return false;
        }

        var segments = timePart.Split(':', StringSplitOptions.None);
        if (segments.Length != expectedSegmentCount ||
            segments.Any(segment => segment.Length is < 1 or > 2 || !IsDigitsOnly(segment)))
        {
            return false;
        }

        var numbers = segments
            .Select(segment => int.Parse(segment, CultureInfo.InvariantCulture))
            .ToArray();
        if (numbers.Any(number => number < 0))
        {
            return false;
        }

        if (expectedSegmentCount == 3)
        {
            if (numbers[1] > 59 || numbers[2] > 59)
            {
                return false;
            }

            value = TimeSpan.FromHours(numbers[0]) +
                    TimeSpan.FromMinutes(numbers[1]) +
                    TimeSpan.FromSeconds(numbers[2]) +
                    TimeSpan.FromMilliseconds(milliseconds);
            return true;
        }

        if (numbers[0] > 59 || numbers[1] > 59)
        {
            return false;
        }

        value = TimeSpan.FromMinutes(numbers[0]) +
                TimeSpan.FromSeconds(numbers[1]) +
                TimeSpan.FromMilliseconds(milliseconds);
        return true;
    }

    private static bool TryParseSecondsOnlyTime(string text, out TimeSpan value)
    {
        value = TimeSpan.Zero;
        if (!TrySplitMilliseconds(text, out var secondsPart, out var milliseconds) ||
            secondsPart.Length is < 1 or > 2 ||
            !IsDigitsOnly(secondsPart))
        {
            return false;
        }

        var seconds = int.Parse(secondsPart, CultureInfo.InvariantCulture);
        if (seconds > 59)
        {
            return false;
        }

        value = TimeSpan.FromSeconds(seconds) + TimeSpan.FromMilliseconds(milliseconds);
        return true;
    }

    private static bool TrySplitMilliseconds(string text, out string timePart, out int milliseconds)
    {
        timePart = string.Empty;
        milliseconds = 0;

        var segments = text.Split('.', StringSplitOptions.None);
        if (segments.Length > 2)
        {
            return false;
        }

        timePart = segments[0];
        if (string.IsNullOrWhiteSpace(timePart))
        {
            return false;
        }

        if (segments.Length == 1)
        {
            return true;
        }

        var fraction = segments[1];
        if (fraction.Length is < 1 or > 3 || !IsDigitsOnly(fraction))
        {
            return false;
        }

        milliseconds = int.Parse(fraction.PadRight(3, '0'), CultureInfo.InvariantCulture);
        return true;
    }

    private static bool IsPotentialCompositeTimeInput(string text, int expectedSegmentCount)
    {
        var dotIndex = text.IndexOf('.');
        if (dotIndex == 0 || text.LastIndexOf('.') != dotIndex)
        {
            return false;
        }

        var mainPart = dotIndex >= 0 ? text[..dotIndex] : text;
        var fraction = dotIndex >= 0 ? text[(dotIndex + 1)..] : string.Empty;
        if (fraction.Length > 3 || !IsDigitsOnly(fraction))
        {
            return false;
        }

        var segments = mainPart.Split(':', StringSplitOptions.None);
        if (segments.Length > expectedSegmentCount)
        {
            return false;
        }

        for (var index = 0; index < segments.Length; index++)
        {
            var segment = segments[index];
            var isTail = index == segments.Length - 1;
            if (segment.Length == 0)
            {
                if (!isTail)
                {
                    return false;
                }

                continue;
            }

            if (segment.Length > 2 || !IsDigitsOnly(segment))
            {
                return false;
            }

            var number = int.Parse(segment, CultureInfo.InvariantCulture);
            var isHourSegment = expectedSegmentCount == 3 && index == 0;
            if (!isHourSegment && number > 59)
            {
                return false;
            }
        }

        if (dotIndex < 0)
        {
            return true;
        }

        return segments.Length == expectedSegmentCount && segments[^1].Length > 0;
    }

    private static bool IsPotentialSecondsOnlyInput(string text)
    {
        if (text.Contains(':', StringComparison.Ordinal))
        {
            return false;
        }

        var dotIndex = text.IndexOf('.');
        if (dotIndex == 0 || text.LastIndexOf('.') != dotIndex)
        {
            return false;
        }

        var secondsPart = dotIndex >= 0 ? text[..dotIndex] : text;
        var fraction = dotIndex >= 0 ? text[(dotIndex + 1)..] : string.Empty;
        if (secondsPart.Length is < 1 or > 2 ||
            !IsDigitsOnly(secondsPart) ||
            fraction.Length > 3 ||
            !IsDigitsOnly(fraction))
        {
            return false;
        }

        return int.Parse(secondsPart, CultureInfo.InvariantCulture) <= 59;
    }

    private static bool IsDigitsOnly(string value) => value.All(char.IsDigit);

    private void ShowExportPreparationProgress()
    {
        _exportProgressStateApplier = () =>
            ApplyExportProgressState(
                true,
                0d,
                GetLocalizedText(
                    "trim.progress.summary.export",
                    "\u7247\u6bb5\u5bfc\u51fa"),
                GetLocalizedText(
                    "trim.progress.detail.preparing",
                    "\u6b63\u5728\u6821\u9a8c\u533a\u95f4\u5e76\u51c6\u5907 FFmpeg \u5bfc\u51fa\u53c2\u6570..."),
                GetLocalizedText(
                    "trim.progress.percent.preparing",
                    "\u51c6\u5907\u4e2d"));

        _exportProgressStateApplier.Invoke();
    }

    private void ShowExportFinishingProgress()
    {
        var percentText = ExportProgressPercentText;
        var progressValue = ExportProgressValue;
        var isIndeterminate = IsExportProgressIndeterminate;
        _exportProgressStateApplier = () =>
            ApplyExportProgressState(
                isIndeterminate,
                progressValue,
                GetLocalizedText(
                    "trim.progress.summary.export",
                    "\u7247\u6bb5\u5bfc\u51fa"),
                GetLocalizedText(
                    "trim.progress.detail.finishing",
                    "\u5bfc\u51fa\u5b8c\u6210\uff0c\u6b63\u5728\u6574\u7406\u7ed3\u679c..."),
                string.IsNullOrWhiteSpace(percentText)
                    ? GetLocalizedText(
                        "trim.progress.percent.running",
                        "\u5bfc\u51fa\u4e2d")
                    : percentText);

        _exportProgressStateApplier.Invoke();
    }

    private void UpdateExportProgress(FFmpegProgressUpdate progress)
    {
        if (progress.ProgressRatio is not double ratio)
        {
            _exportProgressStateApplier = () =>
                ApplyExportProgressState(
                    true,
                    ExportProgressValue,
                    GetLocalizedText(
                        "trim.progress.summary.export",
                        "\u7247\u6bb5\u5bfc\u51fa"),
                    GetLocalizedText(
                        "trim.progress.detail.unknown",
                        "\u6b63\u5728\u5bfc\u51fa\u88c1\u526a\u7247\u6bb5\uff0c\u6682\u65f6\u65e0\u6cd5\u4f30\u7b97\u8fdb\u5ea6\u3002"),
                    GetLocalizedText(
                        "trim.progress.percent.running",
                        "\u5bfc\u51fa\u4e2d"));

            _exportProgressStateApplier.Invoke();
            return;
        }

        var normalized = Math.Clamp(ratio, 0d, 1d);
        var roundedProgressValue = Math.Round(normalized * 100d, 1);
        var percentText = $"{Math.Round(normalized * 100d):0}%";
        var processedDuration = progress.ProcessedDuration;
        _exportProgressStateApplier = () =>
            ApplyExportProgressState(
                false,
                roundedProgressValue,
                GetLocalizedText(
                    "trim.progress.summary.export",
                    "\u7247\u6bb5\u5bfc\u51fa"),
                processedDuration is { } processed
                    ? FormatLocalizedText(
                        "trim.progress.detail.processed",
                        "\u5df2\u5bfc\u51fa {processed} / {duration}",
                        ("processed", FormatSelectionTime(processed)),
                        ("duration", SelectedDurationText))
                    : FormatLocalizedText(
                        "trim.progress.detail.percent",
                        "\u5f53\u524d\u5bfc\u51fa\u8fdb\u5ea6 {percent}",
                        ("percent", percentText)),
                percentText);

        _exportProgressStateApplier.Invoke();
    }

    private void ResetExportProgress()
    {
        _exportProgressStateApplier = null;
        ExportProgressVisibility = Visibility.Collapsed;
        IsExportProgressIndeterminate = false;
        ExportProgressValue = 0d;
        ExportProgressSummaryText = string.Empty;
        ExportProgressDetailText = string.Empty;
        ExportProgressPercentText = string.Empty;
    }

    private void ApplyExportProgressState(
        bool isIndeterminate,
        double progressValue,
        string summaryText,
        string detailText,
        string percentText)
    {
        ExportProgressVisibility = Visibility.Visible;
        IsExportProgressIndeterminate = isIndeterminate;
        ExportProgressValue = progressValue;
        ExportProgressSummaryText = summaryText;
        ExportProgressDetailText = detailText;
        ExportProgressPercentText = percentText;
    }

    private void EnsureOutputDirectoryExists()
    {
        if (HasCustomOutputDirectory)
        {
            Directory.CreateDirectory(OutputDirectory);
        }
    }

    private void TryRevealOutputFile(string outputPath)
    {
        try
        {
            if (_userPreferencesService.Load().RevealOutputFileAfterProcessing)
            {
                _fileRevealService.RevealFile(outputPath);
            }
        }
        catch (Exception exception)
        {
            _logger.Log(LogLevel.Warning, "\u88c1\u526a\u5bfc\u51fa\u6210\u529f\u540e\u5b9a\u4f4d\u8f93\u51fa\u6587\u4ef6\u5931\u8d25\u3002", exception);
        }
    }

    private string NormalizeOutputDirectory(string? outputDirectory)
    {
        if (MediaPathResolver.TryNormalizeOutputDirectory(outputDirectory, out var normalizedDirectory))
        {
            return normalizedDirectory;
        }

        _logger.Log(LogLevel.Warning, "\u68c0\u6d4b\u5230\u65e0\u6548\u7684\u88c1\u526a\u8f93\u51fa\u76ee\u5f55\u914d\u7f6e\uff0c\u5df2\u56de\u9000\u4e3a\u539f\u6587\u4ef6\u5939\u8f93\u51fa\u3002");
        return string.Empty;
    }

    private void SetCurrentMediaKind(TrimMediaKind mediaKind)
    {
        _currentMediaKind = mediaKind;
        _availableOutputFormats = ResolveOutputFormats(mediaKind);
        _selectedOutputFormat = ResolvePreferredOutputFormat(_userPreferencesService.Load().PreferredTrimOutputFormatExtension);
        OnPropertyChanged(nameof(AvailableOutputFormats));
        OnPropertyChanged(nameof(SelectedOutputFormat));
        OnPropertyChanged(nameof(SelectedOutputFormatDescription));
    }

    private IReadOnlyList<OutputFormatOption> ResolveOutputFormats(TrimMediaKind mediaKind) =>
        mediaKind == TrimMediaKind.Audio
            ? _configuration.SupportedAudioOutputFormats.Select(option => option.Localize(_localizationService)).ToArray()
            : _configuration.SupportedTrimOutputFormats.Select(option => option.Localize(_localizationService)).ToArray();

    private string GetDefaultPreviewPreparingMessage() =>
        IsAudioTrim
            ? GetLocalizedText(
                "trim.preview.preparing.audio",
                "\u6b63\u5728\u51c6\u5907\u97f3\u9891\u9884\u89c8...")
            : GetLocalizedText(
                "trim.preview.preparing.video",
                "\u6b63\u5728\u51c6\u5907\u89c6\u9891\u9884\u89c8...");

    private string FormatFileSize(long sizeBytes)
    {
        if (sizeBytes <= 0)
        {
            return GetLocalizedText("trim.mediaInfo.unknown", "\u672a\u77e5");
        }

        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var size = (double)sizeBytes;
        var unitIndex = 0;
        while (size >= 1024d && unitIndex < units.Length - 1)
        {
            size /= 1024d;
            unitIndex++;
        }

        return $"{size:0.##} {units[unitIndex]}";
    }

    private string FormatInfoDuration(TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero)
        {
            return GetLocalizedText("trim.mediaInfo.unknown", "\u672a\u77e5");
        }

        return $"{(int)duration.TotalHours:00}:{duration.Minutes:00}:{duration.Seconds:00}";
    }

    private string ExtractFriendlyFailureMessage(FFmpegExecutionResult result)
    {
        if (!string.IsNullOrWhiteSpace(result.FailureReason))
        {
            return result.FailureReason;
        }

        var lines = (result.StandardError ?? string.Empty)
            .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return lines.LastOrDefault() ?? GetLocalizedText(
            "trim.export.failure.noFfmpegMessage",
            "FFmpeg \u672a\u8fd4\u56de\u53ef\u7528\u9519\u8bef\u4fe1\u606f\u3002");
    }

    private static TimeSpan Clamp(TimeSpan value, TimeSpan min, TimeSpan max) =>
        value < min ? min : value > max ? max : value;

    private static TimeSpan MinTime(TimeSpan left, TimeSpan right) => left <= right ? left : right;

    private static TimeSpan MaxTime(TimeSpan left, TimeSpan right) => left >= right ? left : right;

    private static bool AreClose(TimeSpan left, TimeSpan right) =>
        Math.Abs((left - right).TotalMilliseconds) < 1d;

    internal string FormatTimelineThumbToolTip(TimeSpan duration) => FormatSelectionTime(duration);

    private string FormatSelectionTime(TimeSpan duration)
    {
        if (duration < TimeSpan.Zero)
        {
            duration = TimeSpan.Zero;
        }

        return GetSelectionInputFormat() switch
        {
            SelectionInputFormat.Hours => $"{(int)duration.TotalHours:00}:{duration.Minutes:00}:{duration.Seconds:00}.{duration.Milliseconds:000}",
            SelectionInputFormat.Minutes => $"{(int)duration.TotalMinutes:00}:{duration.Seconds:00}.{duration.Milliseconds:000}",
            _ => $"{(int)duration.TotalSeconds:00}.{duration.Milliseconds:000}"
        };
    }

    private string FormatCompactSelectionTime(TimeSpan duration)
    {
        if (duration < TimeSpan.Zero)
        {
            duration = TimeSpan.Zero;
        }

        return GetSelectionInputFormat() switch
        {
            SelectionInputFormat.Hours => $"{(int)duration.TotalHours:00}:{duration.Minutes:00}:{duration.Seconds:00}",
            SelectionInputFormat.Minutes => $"{(int)duration.TotalMinutes:00}:{duration.Seconds:00}",
            _ => $"{(int)duration.TotalSeconds:00}.{duration.Milliseconds:000}"
        };
    }

    private enum SelectionInputFormat
    {
        Seconds,
        Minutes,
        Hours
    }

    private enum SelectionInputKind
    {
        Start,
        End
    }
}
