using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;

// 功能：主工作区执行进度状态（统一管理批处理进度条与单文件进度文案）
// 模块：视频转换模块 / 音频转换模块
// 说明：可复用，负责进度状态与格式化展示，不直接操作底层执行逻辑。
namespace Vidvix.ViewModels;

public sealed partial class MainViewModel
{
    private Visibility _executionProgressVisibility = Visibility.Collapsed;
    private bool _isExecutionProgressIndeterminate;
    private double _executionProgressValue;
    private string _executionProgressSummaryText = string.Empty;
    private string _executionProgressCurrentItemText = string.Empty;
    private string _executionProgressDetailText = string.Empty;
    private string _executionProgressPercentText = string.Empty;

    public Visibility ExecutionProgressVisibility
    {
        get => _executionProgressVisibility;
        private set => SetProperty(ref _executionProgressVisibility, value);
    }

    public bool IsExecutionProgressIndeterminate
    {
        get => _isExecutionProgressIndeterminate;
        private set => SetProperty(ref _isExecutionProgressIndeterminate, value);
    }

    public double ExecutionProgressValue
    {
        get => _executionProgressValue;
        private set => SetProperty(ref _executionProgressValue, value);
    }

    public string ExecutionProgressSummaryText
    {
        get => _executionProgressSummaryText;
        private set => SetProperty(ref _executionProgressSummaryText, value);
    }

    public string ExecutionProgressCurrentItemText
    {
        get => _executionProgressCurrentItemText;
        private set => SetProperty(ref _executionProgressCurrentItemText, value);
    }

    public string ExecutionProgressDetailText
    {
        get => _executionProgressDetailText;
        private set => SetProperty(ref _executionProgressDetailText, value);
    }

    public string ExecutionProgressPercentText
    {
        get => _executionProgressPercentText;
        private set => SetProperty(ref _executionProgressPercentText, value);
    }

    private void ShowExecutionPreparationProgress(int totalCount) =>
        UpdateExecutionProgress(
            totalCount,
            completedCount: 0,
            currentItem: null,
            currentItemProgressRatio: null,
            processedDuration: null,
            totalDuration: null,
            detailOverride: "\u6b63\u5728\u68c0\u67e5\u5a92\u4f53\u8f68\u9053\u4e0e\u8f93\u51fa\u53c2\u6570...");

    private void UpdateExecutionProgress(
        int totalCount,
        int completedCount,
        MediaJobViewModel? currentItem,
        double? currentItemProgressRatio,
        TimeSpan? processedDuration,
        TimeSpan? totalDuration,
        string? detailOverride = null)
    {
        void ApplyUpdate()
        {
            if (totalCount <= 0)
            {
                HideExecutionProgressCore();
                return;
            }

            var normalizedCompleted = Math.Clamp(completedCount, 0, totalCount);
            ExecutionProgressVisibility = Visibility.Visible;
            ExecutionProgressSummaryText = $"\u5df2\u5b8c\u6210 {normalizedCompleted} / {totalCount}";

            if (currentItem is null)
            {
                ExecutionProgressCurrentItemText = normalizedCompleted >= totalCount
                    ? "\u5f53\u524d\u6587\u4ef6\uff1a\u5df2\u5b8c\u6210"
                    : "\u5f53\u524d\u6587\u4ef6\uff1a\u6b63\u5728\u51c6\u5907...";
                ExecutionProgressValue = Math.Round((normalizedCompleted / (double)totalCount) * 100d, 1);

                if (normalizedCompleted >= totalCount)
                {
                    IsExecutionProgressIndeterminate = false;
                    ExecutionProgressPercentText = "100%";
                    ExecutionProgressDetailText = detailOverride ?? "\u5904\u7406\u5b8c\u6210\uff0c\u6b63\u5728\u6574\u7406\u7ed3\u679c...";
                    return;
                }

                IsExecutionProgressIndeterminate = true;
                ExecutionProgressPercentText = "\u51c6\u5907\u4e2d";
                ExecutionProgressDetailText = detailOverride ?? "\u6b63\u5728\u51c6\u5907\u5904\u7406\u4efb\u52a1...";
                return;
            }

            ExecutionProgressCurrentItemText = $"\u5f53\u524d\u6587\u4ef6\uff1a{currentItem.InputFileName}";

            if (currentItemProgressRatio is double progressRatio)
            {
                var normalizedRatio = Math.Clamp(progressRatio, 0d, 1d);
                var overallProgress = (normalizedCompleted + normalizedRatio) / totalCount;
                IsExecutionProgressIndeterminate = false;
                ExecutionProgressValue = Math.Round(overallProgress * 100d, 1);
                ExecutionProgressPercentText = $"{Math.Round(normalizedRatio * 100d):0}%";
                ExecutionProgressDetailText = detailOverride ?? CreateExecutionProgressDetail(processedDuration, totalDuration, normalizedRatio);
                currentItem.UpdateRunningDetail(CreateRunningItemProgressDetail(processedDuration, totalDuration, normalizedRatio));
                return;
            }

            IsExecutionProgressIndeterminate = true;
            ExecutionProgressValue = Math.Round((normalizedCompleted / (double)totalCount) * 100d, 1);
            ExecutionProgressPercentText = "\u5904\u7406\u4e2d";
            ExecutionProgressDetailText = detailOverride ?? "\u6b63\u5728\u5904\u7406\u5f53\u524d\u6587\u4ef6\uff0c\u6682\u65f6\u65e0\u6cd5\u4f30\u7b97\u603b\u8fdb\u5ea6\u3002";
            currentItem.UpdateRunningDetail(detailOverride ?? "\u6b63\u5728\u5904\u7406\uff0c\u6682\u65f6\u65e0\u6cd5\u4f30\u7b97\u603b\u8fdb\u5ea6\u3002");
        }

        if (_dispatcherService.HasThreadAccess)
        {
            ApplyUpdate();
            return;
        }

        _dispatcherService.TryEnqueue(ApplyUpdate);
    }

    private void ResetExecutionProgress()
    {
        if (_dispatcherService.HasThreadAccess)
        {
            HideExecutionProgressCore();
            return;
        }

        _dispatcherService.TryEnqueue(HideExecutionProgressCore);
    }

    private Task<TimeSpan?> TryGetMediaDurationAsync(string inputPath, CancellationToken cancellationToken) =>
        _mediaProcessingWorkflowService.GetMediaDurationAsync(inputPath, cancellationToken);

    private void HideExecutionProgressCore()
    {
        ExecutionProgressVisibility = Visibility.Collapsed;
        IsExecutionProgressIndeterminate = false;
        ExecutionProgressValue = 0;
        ExecutionProgressSummaryText = string.Empty;
        ExecutionProgressCurrentItemText = string.Empty;
        ExecutionProgressDetailText = string.Empty;
        ExecutionProgressPercentText = string.Empty;
    }

    private static string CreateExecutionProgressDetail(
        TimeSpan? processedDuration,
        TimeSpan? totalDuration,
        double progressRatio)
    {
        var percentText = $"{Math.Round(Math.Clamp(progressRatio, 0d, 1d) * 100d):0}%";

        if (processedDuration is { } processed && totalDuration is { } total && total > TimeSpan.Zero)
        {
            return $"\u5f53\u524d\u6587\u4ef6\u8fdb\u5ea6 {percentText} \u00b7 \u5df2\u5904\u7406 {FormatClockDuration(processed)} / {FormatClockDuration(total)}";
        }

        if (processedDuration is { } processedOnly)
        {
            return $"\u5f53\u524d\u6587\u4ef6\u8fdb\u5ea6 {percentText} \u00b7 \u5df2\u5904\u7406 {FormatClockDuration(processedOnly)}";
        }

        return $"\u5f53\u524d\u6587\u4ef6\u8fdb\u5ea6 {percentText}";
    }

    private static string CreateRunningItemProgressDetail(
        TimeSpan? processedDuration,
        TimeSpan? totalDuration,
        double progressRatio)
    {
        var percentText = $"{Math.Round(Math.Clamp(progressRatio, 0d, 1d) * 100d):0}%";

        if (processedDuration is { } processed && totalDuration is { } total && total > TimeSpan.Zero)
        {
            return $"\u5df2\u5904\u7406 {FormatClockDuration(processed)} / {FormatClockDuration(total)}\uff08{percentText}\uff09";
        }

        if (processedDuration is { } processedOnly)
        {
            return $"\u5df2\u5904\u7406 {FormatClockDuration(processedOnly)}\uff08{percentText}\uff09";
        }

        return $"\u6b63\u5728\u5904\u7406\uff08{percentText}\uff09";
    }

    private static string FormatClockDuration(TimeSpan duration) =>
        duration.TotalHours >= 1
            ? duration.ToString(@"hh\:mm\:ss")
            : duration.ToString(@"mm\:ss");
}
