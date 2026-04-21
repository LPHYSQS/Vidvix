using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Xaml;
using Vidvix.Utils;

namespace Vidvix.ViewModels;

public sealed class TerminalOutputEntryViewModel : ObservableObject
{
    private string _sourceName;
    private string _statusText;
    private string _commandText;
    private string _outputText;
    private Func<string>? _sourceNameResolver;
    private Func<string>? _statusTextResolver;
    private readonly List<OutputLineSegment> _outputLineSegments = new();

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

        if (!string.IsNullOrWhiteSpace(_outputText))
        {
            _outputLineSegments.Add(OutputLineSegment.ForRawText(_outputText));
        }
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
        ApplySourceName(sourceName?.Trim() ?? string.Empty, null);

    public void SetStatusText(string statusText) =>
        ApplyStatusText(statusText?.Trim() ?? string.Empty, null);

    public void SetSourceNameResolver(Func<string> sourceNameResolver)
    {
        ArgumentNullException.ThrowIfNull(sourceNameResolver);
        ApplySourceName(sourceNameResolver(), sourceNameResolver);
    }

    public void SetStatusTextResolver(Func<string> statusTextResolver)
    {
        ArgumentNullException.ThrowIfNull(statusTextResolver);
        ApplyStatusText(statusTextResolver(), statusTextResolver);
    }

    public void SetCommandText(string commandText) =>
        CommandText = commandText?.Trim() ?? string.Empty;

    public void AppendOutputLine(string outputLine)
    {
        var line = outputLine?.TrimEnd();
        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        _outputLineSegments.Add(OutputLineSegment.ForRawText(line));
        RefreshOutputText();
    }

    public void AppendLocalizedOutputLine(Func<string> lineResolver)
    {
        ArgumentNullException.ThrowIfNull(lineResolver);

        var line = lineResolver().TrimEnd();
        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        _outputLineSegments.Add(OutputLineSegment.ForResolver(lineResolver));
        RefreshOutputText();
    }

    public void RefreshLocalization()
    {
        if (_sourceNameResolver is not null)
        {
            SourceName = _sourceNameResolver();
        }

        if (_statusTextResolver is not null)
        {
            StatusText = _statusTextResolver();
        }

        if (_outputLineSegments.Count > 0)
        {
            RefreshOutputText();
        }
    }

    private void ApplySourceName(string sourceName, Func<string>? sourceNameResolver)
    {
        _sourceNameResolver = sourceNameResolver;
        SourceName = sourceName;
    }

    private void ApplyStatusText(string statusText, Func<string>? statusTextResolver)
    {
        _statusTextResolver = statusTextResolver;
        StatusText = statusText;
    }

    private void RefreshOutputText()
    {
        var lines = _outputLineSegments
            .Select(segment => segment.Resolve())
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToArray();

        OutputText = lines.Length == 0
            ? string.Empty
            : string.Join(Environment.NewLine, lines);
    }

    private sealed class OutputLineSegment
    {
        private OutputLineSegment(string text, Func<string>? resolver)
        {
            Text = text;
            Resolver = resolver;
        }

        public string Text { get; }

        public Func<string>? Resolver { get; }

        public static OutputLineSegment ForRawText(string text) =>
            new(text, null);

        public static OutputLineSegment ForResolver(Func<string> resolver) =>
            new(resolver(), resolver);

        public string Resolve() =>
            (Resolver?.Invoke() ?? Text).TrimEnd();
    }
}
