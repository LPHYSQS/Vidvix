using System;
using Microsoft.UI.Xaml;
using Vidvix.Core.Models;

namespace Vidvix.ViewModels;

internal sealed class SplitAudioProgressState
{
    public Visibility ProgressVisibility { get; private set; } = Visibility.Collapsed;

    public bool IsProgressIndeterminate { get; private set; }

    public double ProgressValue { get; private set; }

    public string ProgressSummaryText { get; private set; } = string.Empty;

    public string ProgressDetailText { get; private set; } = string.Empty;

    public string ProgressPercentText { get; private set; } = string.Empty;

    public void Apply(AudioSeparationProgress progress)
    {
        ArgumentNullException.ThrowIfNull(progress);

        ProgressVisibility = Visibility.Visible;
        ProgressSummaryText = progress.StageTitle;
        ProgressDetailText = progress.DetailText;

        if (progress.ProgressRatio is double ratio)
        {
            var normalized = Math.Clamp(ratio, 0d, 1d);
            IsProgressIndeterminate = false;
            ProgressValue = Math.Round(normalized * 100d, 1);
            ProgressPercentText = $"{Math.Round(normalized * 100d):0}%";
            return;
        }

        IsProgressIndeterminate = true;
        ProgressPercentText = "处理中";
    }

    public void ShowPreparation()
    {
        ProgressVisibility = Visibility.Visible;
        IsProgressIndeterminate = true;
        ProgressValue = 0d;
        ProgressSummaryText = "准备开始";
        ProgressDetailText = "正在校验输入并准备拆音运行环境...";
        ProgressPercentText = "准备中";
    }

    public void Reset()
    {
        ProgressVisibility = Visibility.Collapsed;
        IsProgressIndeterminate = false;
        ProgressValue = 0d;
        ProgressSummaryText = string.Empty;
        ProgressDetailText = string.Empty;
        ProgressPercentText = string.Empty;
    }
}
