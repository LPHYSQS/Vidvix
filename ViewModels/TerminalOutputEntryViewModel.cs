using System;
using Microsoft.UI.Xaml;
using Vidvix.Utils;

namespace Vidvix.ViewModels;

public sealed class TerminalOutputEntryViewModel : ObservableObject
{
    private string _sourceName;
    private string _statusText;
    private string _commandText;
    private string _outputText;

    public TerminalOutputEntryViewModel(
        string sourceName,
        string timestampText,
        string statusText,
        string commandText,
        string outputText)
    {
        _sourceName = sourceName?.Trim() ?? string.Empty;
        TimestampText = timestampText?.Trim() ?? string.Empty;
        _statusText = statusText?.Trim() ?? string.Empty;
        _commandText = commandText?.Trim() ?? string.Empty;
        _outputText = outputText?.Trim() ?? string.Empty;
    }

    public string SourceName
    {
        get => _sourceName;
        private set
        {
            if (SetProperty(ref _sourceName, value))
            {
                OnPropertyChanged(nameof(HeaderText));
            }
        }
    }

    public string TimestampText { get; }

    public string StatusText
    {
        get => _statusText;
        private set
        {
            if (SetProperty(ref _statusText, value))
            {
                OnPropertyChanged(nameof(StatusVisibility));
            }
        }
    }

    public string CommandText
    {
        get => _commandText;
        private set
        {
            if (SetProperty(ref _commandText, value))
            {
                OnPropertyChanged(nameof(CommandVisibility));
            }
        }
    }

    public string OutputText
    {
        get => _outputText;
        private set
        {
            if (SetProperty(ref _outputText, value))
            {
                OnPropertyChanged(nameof(OutputVisibility));
            }
        }
    }

    public string HeaderText
    {
        get
        {
            if (string.IsNullOrWhiteSpace(TimestampText))
            {
                return SourceName;
            }

            if (string.IsNullOrWhiteSpace(SourceName))
            {
                return TimestampText;
            }

            return $"{TimestampText} | {SourceName}";
        }
    }

    public Visibility StatusVisibility =>
        string.IsNullOrWhiteSpace(StatusText) ? Visibility.Collapsed : Visibility.Visible;

    public Visibility CommandVisibility =>
        string.IsNullOrWhiteSpace(CommandText) ? Visibility.Collapsed : Visibility.Visible;

    public Visibility OutputVisibility =>
        string.IsNullOrWhiteSpace(OutputText) ? Visibility.Collapsed : Visibility.Visible;

    public void SetSourceName(string sourceName) =>
        SourceName = sourceName?.Trim() ?? string.Empty;

    public void SetStatusText(string statusText) =>
        StatusText = statusText?.Trim() ?? string.Empty;

    public void SetCommandText(string commandText) =>
        CommandText = commandText?.Trim() ?? string.Empty;

    public void AppendOutputLine(string outputLine)
    {
        var line = outputLine?.TrimEnd();
        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        OutputText = string.IsNullOrWhiteSpace(OutputText)
            ? line
            : OutputText + Environment.NewLine + line;
    }
}
