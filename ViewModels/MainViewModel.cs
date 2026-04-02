using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Vidvix.Core.Interfaces;
using Vidvix.Core.Models;
using Vidvix.Utils;

namespace Vidvix.ViewModels;

public sealed class MainViewModel : ObservableObject, IDisposable
{
    private readonly ApplicationConfiguration _configuration;
    private readonly IFFmpegService _ffmpegService;
    private readonly IFFmpegCommandBuilder _ffmpegCommandBuilder;
    private readonly ILogger _logger;
    private readonly IFilePickerService _filePickerService;
    private readonly IDispatcherService _dispatcherService;
    private readonly AsyncRelayCommand _selectInputFileCommand;
    private readonly AsyncRelayCommand _executeConversionCommand;
    private readonly RelayCommand _cancelExecutionCommand;

    private string? _selectedInputFilePath;
    private string? _outputFilePath;
    private string _statusMessage;
    private bool _isBusy;
    private CancellationTokenSource? _executionCancellationSource;
    private bool _isDisposed;

    public MainViewModel(
        ApplicationConfiguration configuration,
        IFFmpegService ffmpegService,
        IFFmpegCommandBuilder ffmpegCommandBuilder,
        ILogger logger,
        IFilePickerService filePickerService,
        IDispatcherService dispatcherService)
    {
        _configuration = configuration;
        _ffmpegService = ffmpegService;
        _ffmpegCommandBuilder = ffmpegCommandBuilder;
        _logger = logger;
        _filePickerService = filePickerService;
        _dispatcherService = dispatcherService;
        _statusMessage = "Ready. Select a source video to begin.";

        LogEntries = new ObservableCollection<LogEntry>();

        foreach (var entry in _logger.Entries)
        {
            LogEntries.Add(entry);
        }

        _logger.EntryLogged += OnEntryLogged;

        _selectInputFileCommand = new AsyncRelayCommand(SelectInputFileAsync, () => !IsBusy);
        _executeConversionCommand = new AsyncRelayCommand(ExecuteConversionAsync, CanExecuteConversion);
        _cancelExecutionCommand = new RelayCommand(CancelExecution, () => IsBusy);

        _logger.Log(LogLevel.Info, "Vidvix is initialized and ready for FFmpeg validation.");
    }

    public ObservableCollection<LogEntry> LogEntries { get; }

    public ICommand SelectInputFileCommand => _selectInputFileCommand;

    public ICommand ExecuteConversionCommand => _executeConversionCommand;

    public ICommand CancelExecutionCommand => _cancelExecutionCommand;

    public string SelectedInputFilePath
    {
        get => _selectedInputFilePath ?? string.Empty;
        private set
        {
            if (SetProperty(ref _selectedInputFilePath, value))
            {
                OutputFilePath = string.IsNullOrWhiteSpace(value)
                    ? string.Empty
                    : MediaPathResolver.CreateSiblingOutputPath(value, _configuration.OutputAudioExtension);

                NotifyCommandStates();
            }
        }
    }

    public string OutputFilePath
    {
        get => _outputFilePath ?? string.Empty;
        private set => SetProperty(ref _outputFilePath, value);
    }

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
                NotifyCommandStates();
            }
        }
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _logger.EntryLogged -= OnEntryLogged;

        _executionCancellationSource?.Cancel();
        _executionCancellationSource?.Dispose();
        _executionCancellationSource = null;
    }

    private async Task SelectInputFileAsync()
    {
        try
        {
            var selectedFilePath = await _filePickerService.PickSingleFileAsync(
                new FilePickerRequest(_configuration.SupportedInputFileTypes, "Select source"));

            if (string.IsNullOrWhiteSpace(selectedFilePath))
            {
                StatusMessage = "File selection was cancelled.";
                _logger.Log(LogLevel.Warning, "File selection was cancelled by the user.");
                return;
            }

            SelectedInputFilePath = selectedFilePath;
            StatusMessage = "Source file selected. Ready to run MP4 to MP3 extraction.";
            _logger.Log(LogLevel.Info, $"Selected input file: {selectedFilePath}");
            _logger.Log(LogLevel.Info, $"Derived output file: {OutputFilePath}");
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "File selection was cancelled.";
            _logger.Log(LogLevel.Warning, "File selection was cancelled.");
        }
        catch (Exception exception)
        {
            StatusMessage = "Unable to select a file.";
            _logger.Log(LogLevel.Error, "An error occurred while selecting a source file.", exception);
        }
    }

    private async Task ExecuteConversionAsync()
    {
        if (!ValidateInputSelection())
        {
            return;
        }

        _executionCancellationSource?.Dispose();
        _executionCancellationSource = new CancellationTokenSource();

        try
        {
            IsBusy = true;
            StatusMessage = "Building the FFmpeg command.";

            var command = BuildAudioExtractionCommand();
            var executionOptions = new FFmpegExecutionOptions
            {
                Timeout = _configuration.DefaultExecutionTimeout
            };

            StatusMessage = "FFmpeg is running asynchronously.";

            var result = await _ffmpegService.ExecuteAsync(
                command,
                executionOptions,
                _executionCancellationSource.Token);

            if (result.WasSuccessful && File.Exists(OutputFilePath))
            {
                StatusMessage = $"Completed successfully. Output: {OutputFilePath}";
                _logger.Log(LogLevel.Info, $"Output file created successfully: {OutputFilePath}");
                return;
            }

            if (result.WasCancelled)
            {
                StatusMessage = "The current FFmpeg task was cancelled.";
                return;
            }

            if (result.TimedOut)
            {
                StatusMessage = "The current FFmpeg task timed out.";
                return;
            }

            StatusMessage = result.FailureReason ?? "FFmpeg execution failed.";
        }
        catch (Exception exception)
        {
            StatusMessage = "An unexpected error interrupted execution.";
            _logger.Log(LogLevel.Error, "An unexpected error interrupted the conversion workflow.", exception);
        }
        finally
        {
            IsBusy = false;
            _executionCancellationSource?.Dispose();
            _executionCancellationSource = null;
        }
    }

    private void CancelExecution()
    {
        if (!IsBusy)
        {
            return;
        }

        StatusMessage = "Cancellation requested.";
        _logger.Log(LogLevel.Warning, "Cancellation requested for the current FFmpeg task.");
        _executionCancellationSource?.Cancel();
    }

    private bool ValidateInputSelection()
    {
        if (string.IsNullOrWhiteSpace(SelectedInputFilePath))
        {
            StatusMessage = "Select a source video before running FFmpeg.";
            _logger.Log(LogLevel.Warning, "Execution was blocked because no source file was selected.");
            return false;
        }

        if (!File.Exists(SelectedInputFilePath))
        {
            StatusMessage = "The selected source file no longer exists.";
            _logger.Log(LogLevel.Error, $"The selected source file does not exist: {SelectedInputFilePath}");
            return false;
        }

        return true;
    }

    private FFmpegCommand BuildAudioExtractionCommand()
    {
        IFFmpegCommandBuilder builder = _ffmpegCommandBuilder
            .Reset()
            .SetExecutablePath(_configuration.FFmpegExecutablePath)
            .SetInput(SelectedInputFilePath)
            .SetOutput(OutputFilePath)
            .AddParameter("-vn");

        if (_configuration.OverwriteOutputFiles)
        {
            builder = builder.AddGlobalParameter("-y");
        }

        return builder.Build();
    }

    private bool CanExecuteConversion() =>
        !IsBusy && !string.IsNullOrWhiteSpace(SelectedInputFilePath);

    private void NotifyCommandStates()
    {
        _selectInputFileCommand.NotifyCanExecuteChanged();
        _executeConversionCommand.NotifyCanExecuteChanged();
        _cancelExecutionCommand.NotifyCanExecuteChanged();
    }

    private void OnEntryLogged(object? sender, LogEntry entry)
    {
        _dispatcherService.TryEnqueue(() => LogEntries.Add(entry));
    }
}
