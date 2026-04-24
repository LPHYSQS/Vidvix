using System;
using Vidvix.Core.Models;
using Vidvix.Utils;

namespace Vidvix.ViewModels;

public sealed class AiEnhancementExecutionState : ObservableObject
{
    private string _stageTitle = string.Empty;
    private string _detailText = string.Empty;
    private double _progressValue;
    private string _lastResultSummary = string.Empty;
    private string _lastOutputPath = string.Empty;
    private bool _hasCompletedResult;

    public string StageTitle
    {
        get => _stageTitle;
        set => SetProperty(ref _stageTitle, value ?? string.Empty);
    }

    public string DetailText
    {
        get => _detailText;
        set => SetProperty(ref _detailText, value ?? string.Empty);
    }

    public double ProgressValue
    {
        get => _progressValue;
        set
        {
            var clampedValue = Math.Clamp(value, 0d, 100d);
            SetProperty(ref _progressValue, clampedValue);
        }
    }

    public string LastResultSummary
    {
        get => _lastResultSummary;
        set => SetProperty(ref _lastResultSummary, value ?? string.Empty);
    }

    public string LastOutputPath
    {
        get => _lastOutputPath;
        set
        {
            if (!SetProperty(ref _lastOutputPath, value ?? string.Empty))
            {
                return;
            }

            OnPropertyChanged(nameof(HasLastOutputPath));
        }
    }

    public bool HasCompletedResult
    {
        get => _hasCompletedResult;
        set => SetProperty(ref _hasCompletedResult, value);
    }

    public bool HasLastOutputPath => !string.IsNullOrWhiteSpace(LastOutputPath);

    public void ResetForExecution(string stageTitle, string detailText)
    {
        StageTitle = stageTitle;
        DetailText = detailText;
        ProgressValue = 0d;
        HasCompletedResult = false;
    }

    public void ApplyProgress(AiEnhancementProgress progress)
    {
        ArgumentNullException.ThrowIfNull(progress);

        StageTitle = progress.StageTitle;
        DetailText = progress.DetailText;
        ProgressValue = (progress.ProgressRatio ?? 0d) * 100d;
    }

    public void ApplySuccess(string summary, string outputPath)
    {
        LastResultSummary = summary;
        LastOutputPath = outputPath;
        HasCompletedResult = true;
        ProgressValue = 100d;
    }

    public void ApplyFailure(string summary)
    {
        LastResultSummary = summary;
        HasCompletedResult = false;
    }
}
