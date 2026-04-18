namespace Vidvix.Core.Models;

public sealed class AudioSeparationProgress
{
    public AudioSeparationProgress(
        string stageTitle,
        string detailText,
        double? progressRatio)
    {
        StageTitle = stageTitle;
        DetailText = detailText;
        ProgressRatio = progressRatio;
    }

    public string StageTitle { get; }

    public string DetailText { get; }

    public double? ProgressRatio { get; }
}
