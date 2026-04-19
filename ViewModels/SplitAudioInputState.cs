using System;
using System.IO;
using Microsoft.UI.Xaml;

namespace Vidvix.ViewModels;

internal sealed class SplitAudioInputState
{
    private const string DefaultInputSummaryText = "支持视频与纯音频输入；如果导入视频，会自动提取主音轨后再开始拆音。";

    public string InputPath { get; private set; } = string.Empty;

    public string InputFileName { get; private set; } = string.Empty;

    public string InputSummaryText { get; private set; } = DefaultInputSummaryText;

    public bool HasInput => !string.IsNullOrWhiteSpace(InputPath);

    public Visibility PlaceholderVisibility => HasInput ? Visibility.Collapsed : Visibility.Visible;

    public Visibility InputCardVisibility => HasInput ? Visibility.Visible : Visibility.Collapsed;

    public void SetSelectedInput(string inputPath, string? inputSummaryText)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputPath);

        InputPath = Path.GetFullPath(inputPath);
        InputFileName = Path.GetFileName(InputPath);
        InputSummaryText = string.IsNullOrWhiteSpace(inputSummaryText)
            ? DefaultInputSummaryText
            : inputSummaryText;
    }

    public void Clear()
    {
        InputPath = string.Empty;
        InputFileName = string.Empty;
        InputSummaryText = DefaultInputSummaryText;
    }
}
