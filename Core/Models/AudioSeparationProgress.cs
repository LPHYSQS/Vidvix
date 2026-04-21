using System;

namespace Vidvix.Core.Models;

public sealed class AudioSeparationProgress
{
    public AudioSeparationProgress(
        string stageTitle,
        string detailText,
        double? progressRatio,
        Func<string>? stageTitleResolver = null,
        Func<string>? detailTextResolver = null)
    {
        StageTitle = stageTitle;
        DetailText = detailText;
        ProgressRatio = progressRatio;
        StageTitleResolver = stageTitleResolver;
        DetailTextResolver = detailTextResolver;
    }

    public string StageTitle { get; }

    public string DetailText { get; }

    public double? ProgressRatio { get; }

    public Func<string>? StageTitleResolver { get; }

    public Func<string>? DetailTextResolver { get; }

    public string ResolveStageTitle() => StageTitleResolver?.Invoke() ?? StageTitle;

    public string ResolveDetailText() => DetailTextResolver?.Invoke() ?? DetailText;
}
