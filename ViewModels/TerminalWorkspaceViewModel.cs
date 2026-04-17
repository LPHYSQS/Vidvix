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
    private readonly HashSet<string> _supportedDropFileExtensions;
    private readonly AsyncRelayCommand _executeCommand;
    private string _commandText = string.Empty;
    private bool _isExecuting;
    private CancellationTokenSource? _executionCancellationSource;

    public TerminalWorkspaceViewModel()
        : this(new ApplicationConfiguration(), null)
    {
    }

    public TerminalWorkspaceViewModel(
        ApplicationConfiguration configuration,
        IFFmpegTerminalService? terminalService)
    {
        ArgumentNullException.ThrowIfNull(configuration);

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

    public string CommandSectionTitle => "命令输入";

    public string CommandSectionDescription =>
        "这里只允许输入 ffmpeg、ffprobe、ffplay 三个命令，并且始终调用软件内置的 FF 程序，不会执行其他 CMD 命令。";

    public string CommandPlaceholder => "例如：ffmpeg -i input.mp4 -vf scale=1280:-2 output.mp4";

    public string ExecuteButtonText => IsExecuting ? "执行中..." : "执行";

    public string OutputSectionTitle => "命令输出";

    public string OutputSectionDescription =>
        "命令的标准输出和错误输出都会实时显示在这里，内容会自动滚动到最新位置。";

    public string EmptyStateTitle => "暂无执行记录";

    public string EmptyStateDescription => "输入 ffmpeg、ffprobe 或 ffplay 命令后，执行输出会实时显示在这里。";

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
            sourceName: "FF 内置终端",
            timestampText: DateTime.Now.ToString("HH:mm:ss"),
            statusText: "执行中",
            commandText: rawCommandText,
            outputText: string.Empty);

        OutputEntries.Add(outputEntry);
        RequestScrollToEnd();

        _executionCancellationSource?.Dispose();
        _executionCancellationSource = new CancellationTokenSource();
        IsExecuting = true;

        try
        {
            if (_terminalService is null)
            {
                outputEntry.SetStatusText("不可用");
                outputEntry.AppendOutputLine("终端服务尚未初始化。");
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

            outputEntry.SetStatusText(CreateStatusText(result));

            if (!string.IsNullOrWhiteSpace(result.FailureReason) &&
                !string.Equals(result.FailureReason, "命令已取消。", StringComparison.Ordinal))
            {
                outputEntry.AppendOutputLine(result.FailureReason);
            }

            RequestScrollToEnd();
        }
        catch (OperationCanceledException)
        {
            outputEntry.SetStatusText("已取消");
            outputEntry.AppendOutputLine("命令已取消。");
            RequestScrollToEnd();
        }
        catch (Exception exception)
        {
            outputEntry.SetStatusText("异常");
            outputEntry.AppendOutputLine($"命令执行时发生异常：{exception.Message}");
            RequestScrollToEnd();
        }
        finally
        {
            IsExecuting = false;
            _executionCancellationSource?.Dispose();
            _executionCancellationSource = null;
        }
    }

    private static string CreateStatusText(TerminalCommandExecutionResult result)
    {
        if (result.WasCancelled)
        {
            return "已取消";
        }

        if (result.WasSuccessful)
        {
            return "已完成";
        }

        if (result.ExitCode is { } exitCode)
        {
            return $"退出 {exitCode}";
        }

        if (!string.IsNullOrWhiteSpace(result.FailureReason) &&
            result.FailureReason.Contains("仅支持", StringComparison.Ordinal))
        {
            return "已拒绝";
        }

        return "执行失败";
    }

    private static string ResolveSourceName(string displayCommandText)
    {
        if (string.IsNullOrWhiteSpace(displayCommandText))
        {
            return "FF 内置终端";
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
}
