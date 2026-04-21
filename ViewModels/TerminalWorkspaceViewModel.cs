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

public sealed class TerminalWorkspaceViewModel : ObservableObject, IDisposable
{
    private readonly IFFmpegTerminalService? _terminalService;
    private readonly ILocalizationService? _localizationService;
    private readonly HashSet<string> _supportedDropFileExtensions;
    private readonly AsyncRelayCommand _executeCommand;
    private string _commandText = string.Empty;
    private bool _isExecuting;
    private CancellationTokenSource? _executionCancellationSource;

    public TerminalWorkspaceViewModel()
        : this(new ApplicationConfiguration(), null, null)
    {
    }

    public TerminalWorkspaceViewModel(
        ApplicationConfiguration configuration,
        ILocalizationService? localizationService,
        IFFmpegTerminalService? terminalService)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        _localizationService = localizationService;
        _terminalService = terminalService;
        _supportedDropFileExtensions = configuration.SupportedVideoInputFileTypes
            .Concat(configuration.SupportedAudioInputFileTypes)
            .Where(static extension => !string.IsNullOrWhiteSpace(extension))
            .Select(static extension => extension.StartsWith(".", StringComparison.Ordinal) ? extension : $".{extension}")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        _executeCommand = new AsyncRelayCommand(ExecuteCommandAsync, CanExecuteCommand);
        OutputEntries.CollectionChanged += OnOutputEntriesChanged;
    }

    public event EventHandler? ScrollToEndRequested;

    public string CommandSectionTitle =>
        GetLocalizedText("terminal.command.sectionTitle", "命令输入");

    public string CommandSectionDescription =>
        GetLocalizedText(
            "terminal.command.description",
            "这里只允许输入 ffmpeg、ffprobe、ffplay 三个命令，并且始终调用软件内置的 FF 程序，不会执行其他 CMD 命令。");

    public string CommandPlaceholder =>
        GetLocalizedText(
            "terminal.command.placeholder",
            "例如：ffmpeg -i input.mp4 -vf scale=1280:-2 output.mp4");

    public string ExecuteButtonText => IsExecuting
        ? GetLocalizedText("terminal.command.execute.running", "执行中...")
        : GetLocalizedText("terminal.command.execute.idle", "执行");

    public string OutputSectionTitle =>
        GetLocalizedText("terminal.output.sectionTitle", "命令输出");

    public string OutputSectionDescription =>
        GetLocalizedText(
            "terminal.output.description",
            "命令的标准输出和错误输出都会实时显示在这里，内容会自动滚动到最新位置。");

    public string EmptyStateTitle =>
        GetLocalizedText("terminal.output.empty.title", "暂无执行记录");

    public string EmptyStateDescription =>
        GetLocalizedText(
            "terminal.output.empty.description",
            "输入 ffmpeg、ffprobe 或 ffplay 命令后，执行输出会实时显示在这里。");

    public ObservableCollection<TerminalOutputEntryViewModel> OutputEntries { get; } = new();

    public ICommand ExecuteCommand => _executeCommand;

    public string CommandText
    {
        get => _commandText;
        set
        {
            if (SetProperty(ref _commandText, value))
            {
                _executeCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool IsExecuting
    {
        get => _isExecuting;
        private set
        {
            if (SetProperty(ref _isExecuting, value))
            {
                OnPropertyChanged(nameof(ExecuteButtonText));
                _executeCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public Visibility OutputEntriesVisibility =>
        OutputEntries.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

    public Visibility EmptyStateVisibility =>
        OutputEntries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    public bool CanAcceptDroppedItem(string path, bool isFolder)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        if (isFolder)
        {
            return true;
        }

        var extension = Path.GetExtension(path);
        return !string.IsNullOrWhiteSpace(extension) &&
               _supportedDropFileExtensions.Contains(extension);
    }

    public void Dispose()
    {
        _executionCancellationSource?.Cancel();
        _executionCancellationSource?.Dispose();
        _executionCancellationSource = null;
        OutputEntries.CollectionChanged -= OnOutputEntriesChanged;
    }

    public void RefreshLocalization()
    {
        OnPropertyChanged(nameof(CommandSectionTitle));
        OnPropertyChanged(nameof(CommandSectionDescription));
        OnPropertyChanged(nameof(CommandPlaceholder));
        OnPropertyChanged(nameof(ExecuteButtonText));
        OnPropertyChanged(nameof(OutputSectionTitle));
        OnPropertyChanged(nameof(OutputSectionDescription));
        OnPropertyChanged(nameof(EmptyStateTitle));
        OnPropertyChanged(nameof(EmptyStateDescription));

        foreach (var outputEntry in OutputEntries)
        {
            outputEntry.RefreshLocalization();
        }
    }

    private bool CanExecuteCommand() =>
        !IsExecuting &&
        !string.IsNullOrWhiteSpace(CommandText);

    private async Task ExecuteCommandAsync()
    {
        var rawCommandText = CommandText.Trim();
        if (string.IsNullOrWhiteSpace(rawCommandText))
        {
            return;
        }

        var outputEntry = new TerminalOutputEntryViewModel(
            sourceName: GetBuiltInSourceName(),
            timestampText: DateTime.Now.ToString("HH:mm:ss"),
            statusText: GetStatusExecutingText(),
            commandText: rawCommandText,
            outputText: string.Empty);
        outputEntry.SetSourceNameResolver(GetBuiltInSourceName);
        outputEntry.SetStatusTextResolver(GetStatusExecutingText);

        OutputEntries.Add(outputEntry);
        RequestScrollToEnd();

        _executionCancellationSource?.Dispose();
        _executionCancellationSource = new CancellationTokenSource();
        IsExecuting = true;

        try
        {
            if (_terminalService is null)
            {
                outputEntry.SetStatusTextResolver(() => GetLocalizedText("terminal.output.status.unavailable", "不可用"));
                outputEntry.AppendLocalizedOutputLine(() => GetLocalizedText(
                    "terminal.output.message.serviceUnavailable",
                    "终端服务尚未初始化。"));
                RequestScrollToEnd();
                return;
            }

            var outputProgress = new Progress<string>(line =>
            {
                outputEntry.AppendOutputLine(line);
                RequestScrollToEnd();
            });

            var result = await _terminalService.ExecuteAsync(
                rawCommandText,
                outputProgress,
                _executionCancellationSource.Token);

            if (!string.IsNullOrWhiteSpace(result.DisplayCommandText))
            {
                outputEntry.SetCommandText(result.DisplayCommandText);
                outputEntry.SetSourceName(ResolveSourceName(result.DisplayCommandText));
            }

            outputEntry.SetStatusTextResolver(CreateStatusTextResolver(result));

            if (!string.IsNullOrWhiteSpace(result.FailureReason))
            {
                if (result.FailureReasonResolver is not null)
                {
                    outputEntry.AppendLocalizedOutputLine(result.FailureReasonResolver);
                }
                else
                {
                    outputEntry.AppendOutputLine(result.FailureReason);
                }
            }

            RequestScrollToEnd();
        }
        catch (OperationCanceledException)
        {
            outputEntry.SetStatusTextResolver(() => GetLocalizedText("terminal.output.status.cancelled", "已取消"));
            outputEntry.AppendLocalizedOutputLine(() => GetLocalizedText(
                "terminal.output.message.cancelled",
                "命令已取消。"));
            RequestScrollToEnd();
        }
        catch (Exception exception)
        {
            var exceptionMessage = exception.Message;
            outputEntry.SetStatusTextResolver(() => GetLocalizedText("terminal.output.status.exception", "异常"));
            outputEntry.AppendLocalizedOutputLine(() => FormatLocalizedText(
                "terminal.output.message.exception",
                $"命令执行时发生异常：{exceptionMessage}",
                ("message", exceptionMessage)));
            RequestScrollToEnd();
        }
        finally
        {
            IsExecuting = false;
            _executionCancellationSource?.Dispose();
            _executionCancellationSource = null;
        }
    }

    private Func<string> CreateStatusTextResolver(TerminalCommandExecutionResult result)
    {
        if (result.WasCancelled)
        {
            return () => GetLocalizedText("terminal.output.status.cancelled", "已取消");
        }

        if (result.WasSuccessful)
        {
            return () => GetLocalizedText("terminal.output.status.completed", "已完成");
        }

        if (result.ExitCode is { } exitCode)
        {
            return () => FormatLocalizedText(
                "terminal.output.status.exitCode",
                $"退出 {exitCode}",
                ("exitCode", exitCode));
        }

        if (result.WasRejected)
        {
            return () => GetLocalizedText("terminal.output.status.rejected", "已拒绝");
        }

        return () => GetLocalizedText("terminal.output.status.failed", "执行失败");
    }

    private string ResolveSourceName(string displayCommandText)
    {
        if (string.IsNullOrWhiteSpace(displayCommandText))
        {
            return GetBuiltInSourceName();
        }

        var separatorIndex = displayCommandText.IndexOf(' ');
        return separatorIndex > 0
            ? displayCommandText[..separatorIndex]
            : displayCommandText;
    }

    private void OnOutputEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(OutputEntriesVisibility));
        OnPropertyChanged(nameof(EmptyStateVisibility));
        RequestScrollToEnd();
    }

    private void RequestScrollToEnd() =>
        ScrollToEndRequested?.Invoke(this, EventArgs.Empty);

    private string GetBuiltInSourceName() =>
        GetLocalizedText("terminal.output.source.builtIn", "FF 内置终端");

    private string GetStatusExecutingText() =>
        GetLocalizedText("terminal.output.status.executing", "执行中");

    private string GetLocalizedText(string key, string fallback) =>
        _localizationService?.GetString(key, fallback) ?? fallback;

    private string FormatLocalizedText(
        string key,
        string fallback,
        params (string Name, object? Value)[] arguments)
    {
        if (_localizationService is null || arguments.Length == 0)
        {
            return _localizationService?.GetString(key, fallback) ?? fallback;
        }

        var localizedArguments = arguments.ToDictionary(
            argument => argument.Name,
            argument => argument.Value,
            StringComparer.Ordinal);
        return _localizationService.Format(key, localizedArguments, fallback);
    }
}
