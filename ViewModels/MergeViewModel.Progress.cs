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
        private set => SetProperty(ref _processingProgressSummaryText, value);
    }

    public string ProcessingProgressDetailText
    {
        get => _processingProgressDetailText;
        private set => SetProperty(ref _processingProgressDetailText, value);
    }

    public string ProcessingProgressPercentText
    {
        get => _processingProgressPercentText;
        private set => SetProperty(ref _processingProgressPercentText, value);
    }

    public bool CanModifyWorkspace => !IsVideoJoinProcessing;

    public Visibility WorkspaceInteractionShieldVisibility =>
        IsVideoJoinProcessing ? Visibility.Visible : Visibility.Collapsed;

    private void ShowProcessingPreparationProgress(string summaryText, string detailText)
    {
        ProcessingProgressVisibility = Visibility.Visible;
        ProcessingProgressSummaryText = summaryText;
        ProcessingProgressDetailText = detailText;
        ProcessingProgressPercentText = "准备中";
        ProcessingProgressValue = 0d;
        IsProcessingProgressIndeterminate = true;
    }

    private void UpdateProcessingProgress(
        string summaryText,
        FFmpegProgressUpdate progress,
        string indeterminateDetailText)
    {
        ProcessingProgressVisibility = Visibility.Visible;
        ProcessingProgressSummaryText = summaryText;

        if (progress.ProgressRatio is not double ratio)
        {
            IsProcessingProgressIndeterminate = true;
            ProcessingProgressValue = 0d;
            ProcessingProgressPercentText = "处理中";
            ProcessingProgressDetailText = indeterminateDetailText;
            return;
        }

        var normalized = Math.Clamp(ratio, 0d, 1d);
        IsProcessingProgressIndeterminate = false;
        ProcessingProgressValue = Math.Round(normalized * 100d, 1);
        ProcessingProgressPercentText = $"{Math.Round(normalized * 100d):0}%";
        ProcessingProgressDetailText = CreateProcessingProgressDetail(
            progress.ProcessedDuration,
            progress.TotalDuration,
            normalized);
    }

    private void ResetProcessingProgress()
    {
        ProcessingProgressVisibility = Visibility.Collapsed;
        IsProcessingProgressIndeterminate = false;
        ProcessingProgressValue = 0d;
        ProcessingProgressSummaryText = string.Empty;
        ProcessingProgressDetailText = string.Empty;
        ProcessingProgressPercentText = string.Empty;
    }

    private static string CreateProcessingProgressDetail(
        TimeSpan? processedDuration,
        TimeSpan? totalDuration,
        double progressRatio)
    {
        var percentText = $"{Math.Round(Math.Clamp(progressRatio, 0d, 1d) * 100d):0}%";

        if (processedDuration is { } processed && totalDuration is { } total && total > TimeSpan.Zero)
        {
            return $"当前进度 {percentText} · 已处理 {FormatProcessingDuration(processed)} / {FormatProcessingDuration(total)}";
        }

        if (processedDuration is { } processedOnly)
        {
            return $"当前进度 {percentText} · 已处理 {FormatProcessingDuration(processedOnly)}";
        }

        return $"当前进度 {percentText}";
    }

    private void HandleVideoJoinProgress(FFmpegProgressUpdate progress, int segmentCount)
    {
        if (progress.IsCompleted)
        {
            return;
        }

        UpdateProcessingProgress(
            "视频拼接",
            progress,
            $"正在拼接 {segmentCount} 段视频，FFmpeg 正在返回实时进度...");

        StatusMessage = BuildLiveProgressStatusMessage(
            progress,
            $"正在拼接 {segmentCount} 段视频",
            "FFmpeg 正在返回实时进度...");
    }

    private void HandleAudioJoinProgress(FFmpegProgressUpdate progress, int segmentCount)
    {
        if (progress.IsCompleted)
        {
            return;
        }

        UpdateProcessingProgress(
            "音频拼接",
            progress,
            $"正在拼接 {segmentCount} 段音频，FFmpeg 正在返回实时进度...");

        StatusMessage = BuildLiveProgressStatusMessage(
            progress,
            $"正在拼接 {segmentCount} 段音频",
            "FFmpeg 正在返回实时进度...");
    }

    private void HandleAudioVideoComposeProgress(FFmpegProgressUpdate progress)
    {
        if (progress.IsCompleted)
        {
            return;
        }

        UpdateProcessingProgress(
            "音视频合成",
            progress,
            "正在合成音视频，FFmpeg 正在返回实时进度...");

        StatusMessage = BuildLiveProgressStatusMessage(
            progress,
            "正在合成音视频",
            "FFmpeg 正在返回实时进度...");
    }

    private static string BuildLiveProgressStatusMessage(
        FFmpegProgressUpdate progress,
        string actionText,
        string indeterminateDetailText)
    {
        if (progress.ProgressRatio is not double ratio)
        {
            return $"{actionText}，{indeterminateDetailText}";
        }

        var normalized = Math.Clamp(ratio, 0d, 1d);
        var percentText = $"{Math.Round(normalized * 100d):0}%";

        return progress.ProcessedDuration is { } processedDuration && progress.TotalDuration is { } totalDuration
            ? $"{actionText}：{percentText}（{FormatProcessingDuration(processedDuration)} / {FormatProcessingDuration(totalDuration)}）"
            : $"{actionText}：{percentText}";
    }

    private static string FormatProcessingDuration(TimeSpan duration) =>
        duration.TotalHours >= 1
            ? duration.ToString(@"hh\:mm\:ss")
            : duration.ToString(@"mm\:ss");
}
