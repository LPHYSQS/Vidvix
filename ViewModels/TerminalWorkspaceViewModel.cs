using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Microsoft.UI.Xaml;
using Vidvix.Utils;

namespace Vidvix.ViewModels;

public sealed class TerminalWorkspaceViewModel : ObservableObject
{
    private string _commandText = string.Empty;

    public TerminalWorkspaceViewModel()
    {
        OutputEntries.CollectionChanged += OnOutputEntriesChanged;
    }

    public string CommandSectionTitle => "命令输入";

    public string CommandSectionDescription =>
        "支持输入 FFmpeg、FFprobe 等媒体处理命令。输入完成后点击“执行”，可在上方查看对应的输出与状态信息。";

    public string CommandPlaceholder => "例如：ffmpeg -i input.mp4 -vf scale=1280:-2 output.mp4";

    public string ExecuteButtonText => "执行";

    public string OutputSectionTitle => "命令输出";

    public string OutputSectionDescription => "执行后的标准输出、标准错误和状态信息会在此集中展示。";

    public string EmptyStateTitle => "暂无执行记录";

    public string EmptyStateDescription => "输入 FFmpeg、FFprobe 等命令并执行后，可在这里查看完整的输出内容。";

    public ObservableCollection<TerminalOutputEntryViewModel> OutputEntries { get; } = new();

    public string CommandText
    {
        get => _commandText;
        set => SetProperty(ref _commandText, value);
    }

    public Visibility OutputEntriesVisibility =>
        OutputEntries.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

    public Visibility EmptyStateVisibility =>
        OutputEntries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    private void OnOutputEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(OutputEntriesVisibility));
        OnPropertyChanged(nameof(EmptyStateVisibility));
    }
}
