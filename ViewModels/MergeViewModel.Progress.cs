using System;
using Microsoft.UI.Xaml;
using Vidvix.Core.Models;

namespace Vidvix.ViewModels;

public sealed partial class MergeViewModel
{
    private Visibility _processingProgressVisibility = Visibility.Collapsed;
    private bool _isProcessingProgressIndeterminate;
    private double _processingProgressValue;
    private string _processingProgressSummaryText = string.Empty;
    private string _processingProgressDetailText = string.Empty;
    private string _processingProgressPercentText = string.Empty;
    private LocalizedTextState? _processingProgressSummaryTextState;
    private LocalizedTextState? _processingProgressDetailTextState;
    private LocalizedTextState? _processingProgressPercentTextState;

    public Visibility ProcessingProgressVisibility
    {
        get => _processingProgressVisibility;
        private set => SetProperty(ref _processingProgressVisibility, value);
    }

    public bool IsProcessingProgressIndeterminate
    {
        get => _isProcessingProgressIndeterminate;
        private set => SetProperty(ref _isProcessingProgressIndeterminate, value);
    }

    public double ProcessingProgressValue
    {
        get => _processingProgressValue;
        private set => SetProperty(ref _processingProgressValue, value);
    }

    public string ProcessingProgressSummaryText
    {
        get => _processingProgressSummaryText;
        private set
        {
            _processingProgressSummaryTextState = null;
            SetProperty(ref _processingProgressSummaryText, value);
        }
    }

    public string ProcessingProgressDetailText
    {
        get => _processingProgressDetailText;
        private set
        {
            _processingProgressDetailTextState = null;
            SetProperty(ref _processingProgressDetailText, value);
        }
    }

    public string ProcessingProgressPercentText
    {
        get => _processingProgressPercentText;
        private set
        {
            _processingProgressPercentTextState = null;
            SetProperty(ref _processingProgressPercentText, value);
        }
    }

    public bool CanModifyWorkspace => !IsVideoJoinProcessing;

    public Visibility WorkspaceInteractionShieldVisibility =>
        IsVideoJoinProcessing ? Visibility.Visible : Visibility.Collapsed;

    private void ShowProcessingPreparationProgress(
        string summaryKey,
        string summaryFallback,
        string detailKey,
        string detailFallback)
    {
        ProcessingProgressVisibility = Visibility.Visible;
        SetProcessingProgressSummaryText(summaryKey, summaryFallback);
        SetProcessingProgressDetailText(detailKey, detailFallback);
        SetProcessingProgressPercentText("merge.progress.percent.preparing", "准备中");
        ProcessingProgressValue = 0d;
        IsProcessingProgressIndeterminate = true;
    }

    private void UpdateProcessingProgress(
        string summaryKey,
        string summaryFallback,
        FFmpegProgressUpdate progress,
        string actionKey,
        string actionFallback,
        params (string Name, object? Value)[] actionArguments)
    {
        ProcessingProgressVisibility = Visibility.Visible;
        SetProcessingProgressSummaryText(summaryKey, summaryFallback);

        if (progress.ProgressRatio is not double ratio)
        {
            IsProcessingProgressIndeterminate = true;
            ProcessingProgressValue = 0d;
            SetProcessingProgressPercentText("merge.progress.percent.processing", "处理中");
            SetProcessingProgressDetailText(
                "merge.progress.detail.indeterminate",
                "{action}，FFmpeg 正在返回实时进度...",
                ("action", FormatLocalizedText(actionKey, actionFallback, actionArguments)));
            return;
        }

        var normalized = Math.Clamp(ratio, 0d, 1d);
        IsProcessingProgressIndeterminate = false;
        ProcessingProgressValue = Math.Round(normalized * 100d, 1);
        var percentText = $"{Math.Round(normalized * 100d):0}%";
        SetProcessingProgressPercentText("merge.progress.percent.value", "{percent}", ("percent", percentText));
        SetProcessingProgressDetailText(
            CreateProcessingProgressDetailState(
            progress.ProcessedDuration,
            progress.TotalDuration,
            normalized));
    }

    private void ResetProcessingProgress()
    {
        ProcessingProgressVisibility = Visibility.Collapsed;
        IsProcessingProgressIndeterminate = false;
        ProcessingProgressValue = 0d;
        _processingProgressSummaryTextState = null;
        _processingProgressDetailTextState = null;
        _processingProgressPercentTextState = null;
        ProcessingProgressSummaryText = string.Empty;
        ProcessingProgressDetailText = string.Empty;
        ProcessingProgressPercentText = string.Empty;
    }

    private LocalizedTextState CreateProcessingProgressDetailState(
        TimeSpan? processedDuration,
        TimeSpan? totalDuration,
        double progressRatio)
    {
        var percentText = $"{Math.Round(Math.Clamp(progressRatio, 0d, 1d) * 100d):0}%";

        if (processedDuration is { } processed && totalDuration is { } total && total > TimeSpan.Zero)
        {
            return new LocalizedTextState(
                "merge.progress.detail.processedTotal",
                "当前进度 {percent} · 已处理 {processed} / {total}",
                ("percent", percentText),
                ("processed", FormatProcessingDuration(processed)),
                ("total", FormatProcessingDuration(total)));
        }

        if (processedDuration is { } processedOnly)
        {
            return new LocalizedTextState(
                "merge.progress.detail.processedOnly",
                "当前进度 {percent} · 已处理 {processed}",
                ("percent", percentText),
                ("processed", FormatProcessingDuration(processedOnly)));
        }

        return new LocalizedTextState(
            "merge.progress.detail.percentOnly",
            "当前进度 {percent}",
            ("percent", percentText));
    }

    private void HandleVideoJoinProgress(FFmpegProgressUpdate progress, int segmentCount)
    {
        if (progress.IsCompleted)
        {
            return;
        }

        UpdateProcessingProgress(
            "merge.progress.videoJoin.summary",
            "视频拼接",
            progress,
            "merge.progress.videoJoin.action",
            "正在拼接 {count} 段视频",
            ("count", segmentCount));

        SetLiveProgressStatusMessage(
            progress,
            "merge.progress.videoJoin.action",
            "正在拼接 {count} 段视频",
            ("count", segmentCount));
    }

    private void HandleAudioJoinProgress(FFmpegProgressUpdate progress, int segmentCount)
    {
        if (progress.IsCompleted)
        {
            return;
        }

        UpdateProcessingProgress(
            "merge.progress.audioJoin.summary",
            "音频拼接",
            progress,
            "merge.progress.audioJoin.action",
            "正在拼接 {count} 段音频",
            ("count", segmentCount));

        SetLiveProgressStatusMessage(
            progress,
            "merge.progress.audioJoin.action",
            "正在拼接 {count} 段音频",
            ("count", segmentCount));
    }

    private void HandleAudioVideoComposeProgress(FFmpegProgressUpdate progress)
    {
        if (progress.IsCompleted)
        {
            return;
        }

        UpdateProcessingProgress(
            "merge.progress.audioVideoCompose.summary",
            "音视频合成",
            progress,
            "merge.progress.audioVideoCompose.action",
            "正在合成音视频");

        SetLiveProgressStatusMessage(
            progress,
            "merge.progress.audioVideoCompose.action",
            "正在合成音视频");
    }

    private void SetLiveProgressStatusMessage(
        FFmpegProgressUpdate progress,
        string actionKey,
        string actionFallback,
        params (string Name, object? Value)[] actionArguments)
    {
        if (progress.ProgressRatio is not double ratio)
        {
            SetStatusMessage(
                "merge.status.progress.indeterminate",
                "{action}，FFmpeg 正在返回实时进度...",
                LocalizedArgument("action", () => FormatLocalizedText(actionKey, actionFallback, actionArguments)));
            return;
        }

        var normalized = Math.Clamp(ratio, 0d, 1d);
        var percentText = $"{Math.Round(normalized * 100d):0}%";

        if (progress.ProcessedDuration is { } processedDuration && progress.TotalDuration is { } totalDuration)
        {
            SetStatusMessage(
                "merge.status.progress.valueWithDuration",
                "{action}：{percent}（{processed} / {total}）",
                LocalizedArgument("action", () => FormatLocalizedText(actionKey, actionFallback, actionArguments)),
                ("percent", percentText),
                ("processed", FormatProcessingDuration(processedDuration)),
                ("total", FormatProcessingDuration(totalDuration)));
            return;
        }

        SetStatusMessage(
            "merge.status.progress.value",
            "{action}：{percent}",
            LocalizedArgument("action", () => FormatLocalizedText(actionKey, actionFallback, actionArguments)),
            ("percent", percentText));
    }

    private void SetProcessingProgressSummaryText(string key, string fallback, params (string Name, object? Value)[] arguments)
    {
        _processingProgressSummaryTextState = new LocalizedTextState(key, fallback, arguments);
        SetProperty(
            ref _processingProgressSummaryText,
            ResolveLocalizedText(_processingProgressSummaryTextState),
            nameof(ProcessingProgressSummaryText));
    }

    private void SetProcessingProgressDetailText(string key, string fallback, params (string Name, object? Value)[] arguments)
    {
        _processingProgressDetailTextState = new LocalizedTextState(key, fallback, arguments);
        SetProperty(
            ref _processingProgressDetailText,
            ResolveLocalizedText(_processingProgressDetailTextState),
            nameof(ProcessingProgressDetailText));
    }

    private void SetProcessingProgressDetailText(LocalizedTextState state)
    {
        _processingProgressDetailTextState = state;
        SetProperty(
            ref _processingProgressDetailText,
            ResolveLocalizedText(_processingProgressDetailTextState),
            nameof(ProcessingProgressDetailText));
    }

    private void SetProcessingProgressPercentText(string key, string fallback, params (string Name, object? Value)[] arguments)
    {
        _processingProgressPercentTextState = new LocalizedTextState(key, fallback, arguments);
        SetProperty(
            ref _processingProgressPercentText,
            ResolveLocalizedText(_processingProgressPercentTextState),
            nameof(ProcessingProgressPercentText));
    }

    private static string FormatProcessingDuration(TimeSpan duration) =>
        duration.TotalHours >= 1
            ? duration.ToString(@"hh\:mm\:ss")
            : duration.ToString(@"mm\:ss");
}
