using System;
using System.Collections.Generic;
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

namespace Vidvix.ViewModels;

public sealed class VideoTrimWorkspaceViewModel : ObservableObject, IDisposable
{
    private static readonly TimeSpan MinimumSelectionLength = TimeSpan.FromMilliseconds(1);

    private readonly ApplicationConfiguration _configuration;
    private readonly IFFmpegRuntimeService _ffmpegRuntimeService;
    private readonly IFFmpegService _ffmpegService;
    private readonly IMediaInfoService _mediaInfoService;
    private readonly IVideoTrimCommandFactory _videoTrimCommandFactory;
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

    private string _statusMessage;
    private string _previewStateMessage;
    private OutputFormatOption? _selectedOutputFormat;
    private string _inputPath = string.Empty;
    private string _inputFileName = string.Empty;
    private string _outputDirectory = string.Empty;
    private string _plannedOutputPath = string.Empty;
    private string _lastImportErrorDetails = string.Empty;
    private TimeSpan _mediaDuration;
    private TimeSpan _selectionStart;
    private TimeSpan _selectionEnd;
    private TimeSpan _currentPosition;
    private double _volume = 0.8d;
    private bool _isBusy;
    private bool _isPreviewReady;
    private bool _isPlaying;
    private CancellationTokenSource? _exportCancellationSource;
    private Visibility _exportProgressVisibility = Visibility.Collapsed;
    private bool _isExportProgressIndeterminate;
    private double _exportProgressValue;
    private string _exportProgressSummaryText = string.Empty;
    private string _exportProgressDetailText = string.Empty;
    private string _exportProgressPercentText = string.Empty;
    private bool _isDisposed;

    public VideoTrimWorkspaceViewModel(
        ApplicationConfiguration configuration,
        IFFmpegRuntimeService ffmpegRuntimeService,
        IFFmpegService ffmpegService,
        IMediaInfoService mediaInfoService,
        IVideoTrimCommandFactory videoTrimCommandFactory,
        IFilePickerService filePickerService,
        IUserPreferencesService userPreferencesService,
        IFileRevealService fileRevealService,
        ILogger logger)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _ffmpegRuntimeService = ffmpegRuntimeService ?? throw new ArgumentNullException(nameof(ffmpegRuntimeService));
        _ffmpegService = ffmpegService ?? throw new ArgumentNullException(nameof(ffmpegService));
        _mediaInfoService = mediaInfoService ?? throw new ArgumentNullException(nameof(mediaInfoService));
        _videoTrimCommandFactory = videoTrimCommandFactory ?? throw new ArgumentNullException(nameof(videoTrimCommandFactory));
        _filePickerService = filePickerService ?? throw new ArgumentNullException(nameof(filePickerService));
        _userPreferencesService = userPreferencesService ?? throw new ArgumentNullException(nameof(userPreferencesService));
        _fileRevealService = fileRevealService ?? throw new ArgumentNullException(nameof(fileRevealService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        var preferences = _userPreferencesService.Load();
        AvailableOutputFormats = _configuration.SupportedTrimOutputFormats;
        _selectedOutputFormat = ResolvePreferredOutputFormat(preferences.PreferredTrimOutputFormatExtension);
        _outputDirectory = NormalizeOutputDirectory(preferences.PreferredTrimOutputDirectory);
        _statusMessage = "\u8bf7\u5bfc\u5165\u89c6\u9891\u6587\u4ef6\u6216\u62d6\u62fd\u5230\u6b64\u5904\u5f00\u59cb\u88c1\u526a\u3002";
        _previewStateMessage = "\u8bf7\u5148\u5bfc\u5165\u4e00\u4e2a\u89c6\u9891\u6587\u4ef6\u3002";

        _selectVideoCommand = new AsyncRelayCommand(SelectVideoAsync, () => !HasInput && !IsBusy);
        _removeVideoCommand = new RelayCommand(RemoveVideo, () => HasInput && !IsBusy);
        _selectOutputDirectoryCommand = new AsyncRelayCommand(SelectOutputDirectoryAsync, () => HasInput && !IsBusy);
        _clearOutputDirectoryCommand = new RelayCommand(ClearOutputDirectory, () => HasCustomOutputDirectory && !IsBusy);
        _exportTrimCommand = new AsyncRelayCommand(ExportTrimAsync, CanExportTrim);
        _cancelExportCommand = new RelayCommand(CancelExport, () => IsBusy);
    }

    public IReadOnlyList<OutputFormatOption> AvailableOutputFormats { get; }

    public ICommand SelectVideoCommand => _selectVideoCommand;

    public ICommand RemoveVideoCommand => _removeVideoCommand;

    public ICommand SelectOutputDirectoryCommand => _selectOutputDirectoryCommand;

    public ICommand ClearOutputDirectoryCommand => _clearOutputDirectoryCommand;

    public ICommand ExportTrimCommand => _exportTrimCommand;

    public ICommand CancelExportCommand => _cancelExportCommand;

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

    public bool IsPreviewReady
    {
        get => _isPreviewReady;
        private set
        {
            if (SetProperty(ref _isPreviewReady, value))
            {
                OnPropertyChanged(nameof(CanPlayPreview));
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
                OnPropertyChanged(nameof(PlayPauseButtonText));
                OnPropertyChanged(nameof(PlayPauseButtonSymbol));
            }
        }
    }

    public bool CanPlayPreview => HasInput && IsPreviewReady && !IsBusy && _mediaDuration > TimeSpan.Zero;

    public Visibility PreviewOverlayVisibility => IsPreviewReady ? Visibility.Collapsed : Visibility.Visible;

    public string PlayPauseButtonText => IsPlaying ? "\u6682\u505c" : "\u64ad\u653e";

    public Symbol PlayPauseButtonSymbol => IsPlaying ? Symbol.Pause : Symbol.Play;

    public string InputPath => _inputPath;

    public string InputFileName => _inputFileName;

    public string SupportedInputFormatsHint =>
        "\u652f\u6301\u5bfc\u5165\u683c\u5f0f\uff08" +
        string.Join("\u3001", _configuration.SupportedTrimInputFileTypes.Select(item => item.TrimStart('.').ToUpperInvariant())) +
        "\uff09";

    public string DragDropCaptionText => "\u5bfc\u5165\u89c6\u9891\u6587\u4ef6";

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
        get => _selectedOutputFormat ?? AvailableOutputFormats.First();
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

    public double TimelineMaximum => Math.Max(1d, _mediaDuration.TotalMilliseconds);

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
                OnPropertyChanged(nameof(VolumeLevel));
                OnPropertyChanged(nameof(VolumePercentText));
            }
        }
    }

    public double VolumeLevel => _volume;

    public string VolumePercentText => $"{Math.Round(VolumePercent):0}%";

    public string CurrentPositionText => FormatTime(_currentPosition);

    public string SelectionStartText => FormatTime(_selectionStart);

    public string SelectionEndText => FormatTime(_selectionEnd);

    public string MediaDurationText => FormatTime(_mediaDuration);

    public TimeSpan SelectedDuration => _selectionEnd > _selectionStart ? _selectionEnd - _selectionStart : TimeSpan.Zero;

    public string SelectedDurationText => FormatTime(SelectedDuration);

    public string SelectionSummaryText => HasInput
        ? $"\u88c1\u526a\u533a\u95f4\uff1a{SelectionStartText} - {SelectionEndText}\uff08\u5171 {SelectedDurationText}\uff09"
        : "\u5bfc\u5165\u89c6\u9891\u540e\u5373\u53ef\u8bbe\u7f6e\u88c1\u526a\u533a\u95f4\u3002";

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
            StatusMessage = "\u5f53\u524d\u6b63\u5728\u5bfc\u51fa\u7247\u6bb5\uff0c\u8bf7\u7b49\u5f85\u5b8c\u6210\u6216\u5148\u53d6\u6d88\u3002";
            return;
        }

        if (HasInput)
        {
            StatusMessage = "\u5f53\u524d\u5df2\u6709\u89c6\u9891\uff0c\u8bf7\u5148\u79fb\u9664\u540e\u518d\u5bfc\u5165\u65b0\u7684\u6587\u4ef6\u3002";
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

        if (paths.Length != 1)
        {
            StatusMessage = "\u88c1\u526a\u6a21\u5757\u4e00\u6b21\u53ea\u80fd\u5bfc\u5165 1 \u4e2a\u89c6\u9891\u6587\u4ef6\u3002";
            return;
        }

        var inputPath = paths[0];
        if (Directory.Exists(inputPath))
        {
            StatusMessage = "\u88c1\u526a\u6a21\u5757\u4ec5\u652f\u6301\u5bfc\u5165\u5355\u4e2a\u89c6\u9891\u6587\u4ef6\uff0c\u4e0d\u652f\u6301\u6587\u4ef6\u5939\u3002";
            return;
        }

        if (!File.Exists(inputPath) ||
            !_configuration.SupportedTrimInputFileTypes.Contains(Path.GetExtension(inputPath), StringComparer.OrdinalIgnoreCase))
        {
            StatusMessage = "\u5f53\u524d\u6587\u4ef6\u7c7b\u578b\u4e0d\u5728\u88c1\u526a\u6a21\u5757\u652f\u6301\u8303\u56f4\u5185\u3002";
            return;
        }

        StatusMessage = "\u6b63\u5728\u89e3\u6790\u89c6\u9891\u4fe1\u606f...";
        LastImportErrorDetails = string.Empty;
        SetPreviewPreparing("\u6b63\u5728\u51c6\u5907\u89c6\u9891\u9884\u89c8...");

        var details = await _mediaInfoService.GetMediaDetailsAsync(inputPath);
        var duration = details.Snapshot?.MediaDuration;
        if (!details.IsSuccess ||
            details.Snapshot is null ||
            !details.Snapshot.HasVideoStream ||
            duration is null ||
            duration <= TimeSpan.Zero)
        {
            ApplyImportFailure(
                ResolveImportFailureMessage(details),
                details.DiagnosticDetails);
            return;
        }

        _inputPath = inputPath;
        _inputFileName = Path.GetFileName(inputPath);
        _mediaDuration = duration.Value;
        _selectionStart = TimeSpan.Zero;
        _selectionEnd = duration.Value;
        _currentPosition = TimeSpan.Zero;
        IsPlaying = false;
        RaiseTrimStateChanged();
        RefreshPlannedOutputPath();
        LastImportErrorDetails = string.Empty;
        StatusMessage = $"\u5df2\u5bfc\u5165 {_inputFileName}\uff0c\u8bf7\u62d6\u52a8\u5165\u70b9\u548c\u51fa\u70b9\u786e\u8ba4\u88c1\u526a\u8303\u56f4\u3002";
    }

    public void ApplyPlayableDuration(TimeSpan duration)
    {
        if (!HasInput || duration <= TimeSpan.Zero || AreClose(_mediaDuration, duration))
        {
            return;
        }

        var keepFullRange = _selectionStart == TimeSpan.Zero && AreClose(_selectionEnd, _mediaDuration);
        _mediaDuration = duration;

        if (keepFullRange || _selectionEnd > duration)
        {
            _selectionEnd = duration;
        }

        if (_currentPosition > duration)
        {
            _currentPosition = duration;
        }

        RaiseTimelineChanged();
    }

    public void SetPreviewPreparing(string message)
    {
        IsPreviewReady = false;
        PreviewStateMessage = string.IsNullOrWhiteSpace(message)
            ? "\u6b63\u5728\u51c6\u5907\u89c6\u9891\u9884\u89c8..."
            : message;
    }

    public void SetPreviewReady()
    {
        IsPreviewReady = true;
        PreviewStateMessage = string.Empty;
    }

    public void SetPreviewFailed(string message)
    {
        IsPreviewReady = false;
        IsPlaying = false;
        PreviewStateMessage = string.IsNullOrWhiteSpace(message)
            ? "\u5f53\u524d\u6587\u4ef6\u6682\u65f6\u65e0\u6cd5\u9884\u89c8\u3002"
            : message;
    }

    public void SetPlaying(bool isPlaying) => IsPlaying = isPlaying && CanPlayPreview;

    public void SyncCurrentPosition(TimeSpan position) => SetCurrentPosition(position);

    public void EnsureCurrentPositionWithinSelection()
    {
        if (_currentPosition < _selectionStart || _currentPosition > _selectionEnd)
        {
            SetCurrentPosition(_selectionStart);
        }
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
    }

    private async Task SelectVideoAsync()
    {
        try
        {
            var file = await _filePickerService.PickSingleFileAsync(
                new FilePickerRequest(_configuration.SupportedTrimInputFileTypes, "\u5bfc\u5165\u89c6\u9891"));

            if (string.IsNullOrWhiteSpace(file))
            {
                StatusMessage = "\u5df2\u53d6\u6d88\u5bfc\u5165\u89c6\u9891\u3002";
                return;
            }

            await ImportPathsAsync(new[] { file });
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "\u5df2\u53d6\u6d88\u5bfc\u5165\u89c6\u9891\u3002";
        }
        catch (Exception exception)
        {
            ApplyImportFailure(
                ExtractFriendlyExceptionMessage(exception),
                BuildExceptionDiagnosticDetails(exception));
            _logger.Log(LogLevel.Error, "\u88c1\u526a\u6a21\u5757\u9009\u62e9\u89c6\u9891\u65f6\u53d1\u751f\u5f02\u5e38\u3002", exception);
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
        _plannedOutputPath = string.Empty;
        _mediaDuration = TimeSpan.Zero;
        _selectionStart = TimeSpan.Zero;
        _selectionEnd = TimeSpan.Zero;
        _currentPosition = TimeSpan.Zero;
        IsPlaying = false;
        LastImportErrorDetails = string.Empty;
        SetPreviewFailed("\u8bf7\u5148\u5bfc\u5165\u89c6\u9891\u6587\u4ef6\u6216\u62d6\u62fd\u5230\u6b64\u5904\u5f00\u59cb\u88c1\u526a\u3002");
        RaiseTrimStateChanged();
        StatusMessage = "\u5df2\u79fb\u9664\u5f53\u524d\u89c6\u9891\u3002";
    }

    private async Task SelectOutputDirectoryAsync()
    {
        try
        {
            var folder = await _filePickerService.PickFolderAsync("\u9009\u62e9\u88c1\u526a\u8f93\u51fa\u76ee\u5f55");
            if (string.IsNullOrWhiteSpace(folder))
            {
                StatusMessage = "\u5df2\u53d6\u6d88\u9009\u62e9\u8f93\u51fa\u76ee\u5f55\u3002";
                return;
            }

            OutputDirectory = folder;
            StatusMessage = $"\u5df2\u5c06\u88c1\u526a\u8f93\u51fa\u76ee\u5f55\u8bbe\u7f6e\u4e3a\uff1a{OutputDirectory}";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "\u5df2\u53d6\u6d88\u9009\u62e9\u8f93\u51fa\u76ee\u5f55\u3002";
        }
        catch (Exception exception)
        {
            StatusMessage = "\u9009\u62e9\u8f93\u51fa\u76ee\u5f55\u5931\u8d25\u3002";
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
        StatusMessage = "\u5df2\u6e05\u7a7a\u88c1\u526a\u8f93\u51fa\u76ee\u5f55\uff0c\u5bfc\u51fa\u65f6\u5c06\u4f7f\u7528\u539f\u89c6\u9891\u6240\u5728\u6587\u4ef6\u5939\u3002";
    }

    private async Task ExportTrimAsync()
    {
        if (!CanExportTrim())
        {
            StatusMessage = HasInput
                ? "\u5f53\u524d\u88c1\u526a\u533a\u95f4\u65e0\u6548\uff0c\u8bf7\u91cd\u65b0\u8c03\u6574\u5165\u70b9\u548c\u51fa\u70b9\u3002"
                : "\u8bf7\u5148\u5bfc\u5165\u4e00\u4e2a\u89c6\u9891\u6587\u4ef6\u3002";
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
            StatusMessage = "\u6b63\u5728\u51c6\u5907\u5bfc\u51fa\u88c1\u526a\u7247\u6bb5...";

            var runtime = await _ffmpegRuntimeService.EnsureAvailableAsync(_exportCancellationSource.Token);
            var request = new VideoTrimExportRequest(_inputPath, PlannedOutputPath, _selectionStart, _selectionEnd, SelectedOutputFormat);
            var command = _videoTrimCommandFactory.Create(request, runtime.ExecutablePath);
            var options = new FFmpegExecutionOptions
            {
                Timeout = _configuration.DefaultExecutionTimeout,
                InputDuration = request.Duration,
                Progress = new Progress<FFmpegProgressUpdate>(UpdateExportProgress)
            };

            var result = await _ffmpegService.ExecuteAsync(command, options, _exportCancellationSource.Token);
            if (result.WasSuccessful && File.Exists(request.OutputPath))
            {
                ExportProgressValue = 100d;
                ExportProgressPercentText = "100%";
                ExportProgressDetailText = "\u5bfc\u51fa\u5b8c\u6210\uff0c\u6b63\u5728\u6574\u7406\u7ed3\u679c...";
                StatusMessage = $"\u88c1\u526a\u5bfc\u51fa\u5b8c\u6210\uff1a{Path.GetFileName(request.OutputPath)}";
                TryRevealOutputFile(request.OutputPath);
                return;
            }

            StatusMessage = result.WasCancelled
                ? "\u5df2\u53d6\u6d88\u88c1\u526a\u5bfc\u51fa\u3002"
                : $"\u88c1\u526a\u5bfc\u51fa\u5931\u8d25\uff1a{ExtractFriendlyFailureMessage(result)}";
        }
        catch (OperationCanceledException) when (_exportCancellationSource?.IsCancellationRequested == true)
        {
            StatusMessage = "\u5df2\u53d6\u6d88\u88c1\u526a\u5bfc\u51fa\u3002";
        }
        catch (Exception exception)
        {
            StatusMessage = "\u88c1\u526a\u5bfc\u51fa\u8fc7\u7a0b\u4e2d\u53d1\u751f\u5f02\u5e38\u3002";
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
        OnPropertyChanged(nameof(PlaceholderVisibility));
        OnPropertyChanged(nameof(EditorVisibility));
        OnPropertyChanged(nameof(CanPlayPreview));
        OnPropertyChanged(nameof(InputPath));
        OnPropertyChanged(nameof(InputFileName));
        RaiseTimelineChanged();
        NotifyCommandStates();
    }

    private void RaiseTimelineChanged()
    {
        OnPropertyChanged(nameof(TimelineMaximum));
        OnPropertyChanged(nameof(CurrentPositionMilliseconds));
        OnPropertyChanged(nameof(CurrentPositionText));
        OnPropertyChanged(nameof(SelectionStartMilliseconds));
        OnPropertyChanged(nameof(SelectionEndMilliseconds));
        OnPropertyChanged(nameof(SelectionStartText));
        OnPropertyChanged(nameof(SelectionEndText));
        OnPropertyChanged(nameof(MediaDurationText));
        OnPropertyChanged(nameof(SelectedDuration));
        OnPropertyChanged(nameof(SelectedDurationText));
        OnPropertyChanged(nameof(SelectionSummaryText));
        NotifyCommandStates();
    }

    private void RefreshPlannedOutputPath()
    {
        PlannedOutputPath = HasInput
            ? CreateUniqueOutputPath(
                VideoTrimPathResolver.CreateOutputPath(
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
            PreferredTrimOutputDirectory = HasCustomOutputDirectory ? OutputDirectory : null
        });
    }

    private OutputFormatOption ResolvePreferredOutputFormat(string? extension) =>
        AvailableOutputFormats.FirstOrDefault(item => string.Equals(item.Extension, extension, StringComparison.OrdinalIgnoreCase))
        ?? AvailableOutputFormats.First();

    private void ApplyImportFailure(string message, string? diagnosticDetails)
    {
        var friendlyMessage = string.IsNullOrWhiteSpace(message)
            ? "\u65e0\u6cd5\u89e3\u6790\u5f53\u524d\u89c6\u9891\u6587\u4ef6\u3002"
            : message.Trim();
        var diagnosticText = string.IsNullOrWhiteSpace(diagnosticDetails)
            ? string.Empty
            : diagnosticDetails.Trim();

        StatusMessage = $"\u5bfc\u5165\u89c6\u9891\u5931\u8d25\uff1a{friendlyMessage}";
        LastImportErrorDetails = diagnosticText;
        SetPreviewFailed(
            string.IsNullOrWhiteSpace(diagnosticText)
                ? "\u5f53\u524d\u89c6\u9891\u65e0\u6cd5\u9884\u89c8\uff0c\u8bf7\u68c0\u67e5\u6587\u4ef6\u540e\u91cd\u8bd5\u3002"
                : "\u5f53\u524d\u89c6\u9891\u5bfc\u5165\u5931\u8d25\uff0c\u8bf7\u68c0\u67e5\u4e0b\u65b9\u8be6\u7ec6\u9519\u8bef\u4fe1\u606f\u3002");

        if (!string.IsNullOrWhiteSpace(diagnosticText))
        {
            _logger.Log(LogLevel.Warning, $"\u88c1\u526a\u6a21\u5757\u5bfc\u5165\u89c6\u9891\u5931\u8d25\u8be6\u60c5\uff1a{Environment.NewLine}{diagnosticText}");
        }
    }

    private static string ExtractFriendlyExceptionMessage(Exception exception) =>
        string.IsNullOrWhiteSpace(exception.Message)
            ? "\u5bfc\u5165\u89c6\u9891\u8fc7\u7a0b\u4e2d\u53d1\u751f\u672a\u77e5\u5f02\u5e38\u3002"
            : exception.Message.Trim();

    private static string BuildExceptionDiagnosticDetails(Exception exception)
    {
        var details = exception.ToString().Trim();
        return string.IsNullOrWhiteSpace(details)
            ? "\u672a\u80fd\u6355\u83b7\u5230\u989d\u5916\u7684\u5f02\u5e38\u8bca\u65ad\u4fe1\u606f\u3002"
            : details;
    }

    private static string ResolveImportFailureMessage(MediaDetailsLoadResult result)
    {
        if (!string.IsNullOrWhiteSpace(result.ErrorMessage))
        {
            return result.ErrorMessage;
        }

        if (result.Snapshot is null)
        {
            return "\u65e0\u6cd5\u89e3\u6790\u5f53\u524d\u89c6\u9891\u6587\u4ef6\u3002";
        }

        if (!result.Snapshot.HasVideoStream)
        {
            return "\u5f53\u524d\u6587\u4ef6\u4e0d\u5305\u542b\u53ef\u88c1\u526a\u7684\u89c6\u9891\u6d41\u3002";
        }

        return "\u5f53\u524d\u89c6\u9891\u65f6\u957f\u65e0\u6548\uff0c\u65e0\u6cd5\u5f00\u59cb\u88c1\u526a\u3002";
    }

    private void SetCurrentPosition(TimeSpan position)
    {
        var max = _mediaDuration > TimeSpan.Zero ? _mediaDuration : TimeSpan.Zero;
        var normalized = Clamp(position, TimeSpan.Zero, max);
        if (SetProperty(ref _currentPosition, normalized))
        {
            OnPropertyChanged(nameof(CurrentPositionMilliseconds));
            OnPropertyChanged(nameof(CurrentPositionText));
        }
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

            RaiseTimelineChanged();
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
            RaiseTimelineChanged();
        }
    }

    private TimeSpan GetMinimumRange() => _mediaDuration < MinimumSelectionLength ? _mediaDuration : MinimumSelectionLength;

    private void ShowExportPreparationProgress()
    {
        ExportProgressVisibility = Visibility.Visible;
        ExportProgressSummaryText = "\u88c1\u526a\u5bfc\u51fa";
        ExportProgressDetailText = "\u6b63\u5728\u6821\u9a8c\u533a\u95f4\u5e76\u51c6\u5907 FFmpeg \u5bfc\u51fa\u53c2\u6570...";
        ExportProgressPercentText = "\u51c6\u5907\u4e2d";
        ExportProgressValue = 0d;
        IsExportProgressIndeterminate = true;
    }

    private void UpdateExportProgress(FFmpegProgressUpdate progress)
    {
        ExportProgressVisibility = Visibility.Visible;

        if (progress.ProgressRatio is not double ratio)
        {
            ExportProgressDetailText = "\u6b63\u5728\u5bfc\u51fa\u88c1\u526a\u7247\u6bb5\uff0c\u6682\u65f6\u65e0\u6cd5\u4f30\u7b97\u8fdb\u5ea6\u3002";
            ExportProgressPercentText = "\u5bfc\u51fa\u4e2d";
            IsExportProgressIndeterminate = true;
            return;
        }

        var normalized = Math.Clamp(ratio, 0d, 1d);
        IsExportProgressIndeterminate = false;
        ExportProgressValue = Math.Round(normalized * 100d, 1);
        ExportProgressPercentText = $"{Math.Round(normalized * 100d):0}%";
        ExportProgressDetailText = progress.ProcessedDuration is { } processed
            ? $"\u5df2\u5bfc\u51fa {FormatTime(processed)} / {SelectedDurationText}"
            : $"\u5f53\u524d\u5bfc\u51fa\u8fdb\u5ea6 {ExportProgressPercentText}";
    }

    private void ResetExportProgress()
    {
        ExportProgressVisibility = Visibility.Collapsed;
        IsExportProgressIndeterminate = false;
        ExportProgressValue = 0d;
        ExportProgressSummaryText = string.Empty;
        ExportProgressDetailText = string.Empty;
        ExportProgressPercentText = string.Empty;
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

    private string CreateUniqueOutputPath(string outputPath)
    {
        var directory = Path.GetDirectoryName(outputPath)
            ?? throw new InvalidOperationException("\u88c1\u526a\u8f93\u51fa\u8def\u5f84\u7f3a\u5c11\u6709\u6548\u76ee\u5f55\u3002");
        var fileName = Path.GetFileNameWithoutExtension(outputPath);
        var extension = Path.GetExtension(outputPath);
        var candidate = outputPath;

        for (var index = 2; File.Exists(candidate) || Directory.Exists(candidate); index++)
        {
            candidate = Path.Combine(directory, $"{fileName}_{index}{extension}");
        }

        return candidate;
    }

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
            _logger.Log(LogLevel.Warning, "\u68c0\u6d4b\u5230\u65e0\u6548\u7684\u88c1\u526a\u8f93\u51fa\u76ee\u5f55\u914d\u7f6e\uff0c\u5df2\u56de\u9000\u4e3a\u539f\u6587\u4ef6\u5939\u8f93\u51fa\u3002", exception);
            return string.Empty;
        }
    }

    private static string ExtractFriendlyFailureMessage(FFmpegExecutionResult result)
    {
        if (!string.IsNullOrWhiteSpace(result.FailureReason))
        {
            return result.FailureReason;
        }

        var lines = (result.StandardError ?? string.Empty)
            .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return lines.LastOrDefault() ?? "FFmpeg \u672a\u8fd4\u56de\u53ef\u7528\u9519\u8bef\u4fe1\u606f\u3002";
    }

    private static TimeSpan Clamp(TimeSpan value, TimeSpan min, TimeSpan max) =>
        value < min ? min : value > max ? max : value;

    private static TimeSpan MinTime(TimeSpan left, TimeSpan right) => left <= right ? left : right;

    private static TimeSpan MaxTime(TimeSpan left, TimeSpan right) => left >= right ? left : right;

    private static bool AreClose(TimeSpan left, TimeSpan right) =>
        Math.Abs((left - right).TotalMilliseconds) < 1d;

    private static string FormatTime(TimeSpan duration)
    {
        if (duration < TimeSpan.Zero)
        {
            duration = TimeSpan.Zero;
        }

        var totalHours = (int)duration.TotalHours;
        return totalHours >= 1
            ? $"{totalHours:00}:{duration.Minutes:00}:{duration.Seconds:00}.{duration.Milliseconds:000}"
            : $"{duration.Minutes:00}:{duration.Seconds:00}.{duration.Milliseconds:000}";
    }
}
