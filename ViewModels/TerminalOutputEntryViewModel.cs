using Microsoft.UI.Xaml;

namespace Vidvix.ViewModels;

public sealed class TerminalOutputEntryViewModel
{
    public TerminalOutputEntryViewModel(
        string sourceName,
        string timestampText,
        string statusText,
        string commandText,
        string outputText)
    {
        SourceName = sourceName?.Trim() ?? string.Empty;
        TimestampText = timestampText?.Trim() ?? string.Empty;
        StatusText = statusText?.Trim() ?? string.Empty;
        CommandText = commandText?.Trim() ?? string.Empty;
        OutputText = outputText?.Trim() ?? string.Empty;
    }

    public string SourceName { get; }

    public string TimestampText { get; }

    public string StatusText { get; }

    public string CommandText { get; }

    public string OutputText { get; }

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

            return $"{TimestampText} · {SourceName}";
        }
    }

    public Visibility StatusVisibility =>
        string.IsNullOrWhiteSpace(StatusText) ? Visibility.Collapsed : Visibility.Visible;

    public Visibility CommandVisibility =>
        string.IsNullOrWhiteSpace(CommandText) ? Visibility.Collapsed : Visibility.Visible;

    public Visibility OutputVisibility =>
        string.IsNullOrWhiteSpace(OutputText) ? Visibility.Collapsed : Visibility.Visible;
}
