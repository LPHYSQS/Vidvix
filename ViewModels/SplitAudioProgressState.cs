using System;
using Microsoft.UI.Xaml;
using Vidvix.Core.Interfaces;
using Vidvix.Core.Models;

namespace Vidvix.ViewModels;

internal sealed class SplitAudioProgressState
{
    private readonly ILocalizationService _localizationService;
    private AudioSeparationProgress? _currentProgress;
    private bool _isPreparationVisible;

    public SplitAudioProgressState(ILocalizationService localizationService)
    {
        _localizationService = localizationService ?? throw new ArgumentNullException(nameof(localizationService));
    }

    public Visibility ProgressVisibility { get; private set; } = Visibility.Collapsed;

    public bool IsProgressIndeterminate { get; private set; }

    public double ProgressValue { get; private set; }

    public string ProgressSummaryText { get; private set; } = string.Empty;

    public string ProgressDetailText { get; private set; } = string.Empty;

    public string ProgressPercentText { get; private set; } = string.Empty;

    public void Apply(AudioSeparationProgress progress)
    {
        ArgumentNullException.ThrowIfNull(progress);

        _currentProgress = progress;
        _isPreparationVisible = false;
        ProgressVisibility = Visibility.Visible;
        ProgressSummaryText = progress.ResolveStageTitle();
        ProgressDetailText = progress.ResolveDetailText();

        if (progress.ProgressRatio is double ratio)
        {
            var normalized = Math.Clamp(ratio, 0d, 1d);
            IsProgressIndeterminate = false;
            ProgressValue = Math.Round(normalized * 100d, 1);
            ProgressPercentText = $"{Math.Round(normalized * 100d):0}%";
            return;
        }

        IsProgressIndeterminate = true;
        ProgressPercentText = GetLocalizedText("splitAudio.progress.percent.running", "处理中");
    }

    public void ShowPreparation()
    {
        _currentProgress = null;
        _isPreparationVisible = true;
        ProgressVisibility = Visibility.Visible;
        IsProgressIndeterminate = true;
        ProgressValue = 0d;
        ProgressSummaryText = GetLocalizedText("splitAudio.progress.summary.preparing", "准备开始");
        ProgressDetailText = GetLocalizedText("splitAudio.progress.detail.preparing", "正在校验输入并准备拆音运行环境...");
        ProgressPercentText = GetLocalizedText("splitAudio.progress.percent.preparing", "准备中");
    }

    public void RefreshLocalization()
    {
        if (_currentProgress is not null)
        {
            Apply(_currentProgress);
            return;
        }

        if (_isPreparationVisible && ProgressVisibility == Visibility.Visible)
        {
            ShowPreparation();
        }
    }

    public void Reset()
    {
        _currentProgress = null;
        _isPreparationVisible = false;
        ProgressVisibility = Visibility.Collapsed;
        IsProgressIndeterminate = false;
        ProgressValue = 0d;
        ProgressSummaryText = string.Empty;
        ProgressDetailText = string.Empty;
        ProgressPercentText = string.Empty;
    }

    private string GetLocalizedText(string key, string fallback) =>
        _localizationService.GetString(key, fallback);
}
