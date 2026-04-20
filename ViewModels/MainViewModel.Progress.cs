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
    private ExecutionProgressSnapshot _executionProgressSnapshot = ExecutionProgressSnapshot.Hidden;

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
            detailOverride: LocalizedProgressText.Create(
                "mainWindow.progress.detail.validating",
                "正在检查媒体轨道与输出参数..."));

    private void UpdateExecutionProgress(
        int totalCount,
        int completedCount,
        MediaJobViewModel? currentItem,
        double? currentItemProgressRatio,
        TimeSpan? processedDuration,
        TimeSpan? totalDuration,
        LocalizedProgressText? detailOverride = null)
    {
        void ApplyUpdate()
        {
            var snapshot = totalCount <= 0
                ? ExecutionProgressSnapshot.Hidden
                : new ExecutionProgressSnapshot(
                    totalCount,
                    completedCount,
                    currentItem,
                    currentItemProgressRatio,
                    processedDuration,
                    totalDuration,
                    detailOverride);

            _executionProgressSnapshot = snapshot;
            ApplyExecutionProgressSnapshot(snapshot);
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
            _executionProgressSnapshot = ExecutionProgressSnapshot.Hidden;
            HideExecutionProgressCore();
            return;
        }

        _dispatcherService.TryEnqueue(() =>
        {
            _executionProgressSnapshot = ExecutionProgressSnapshot.Hidden;
            HideExecutionProgressCore();
        });
    }

    private void RefreshExecutionProgressLocalization()
    {
        if (_executionProgressSnapshot == ExecutionProgressSnapshot.Hidden)
        {
            return;
        }

        ApplyExecutionProgressSnapshot(_executionProgressSnapshot);
    }

    private Task<TimeSpan?> TryGetMediaDurationAsync(string inputPath, CancellationToken cancellationToken) =>
        _mediaProcessingWorkflowService.GetMediaDurationAsync(inputPath, cancellationToken);

    private void ApplyExecutionProgressSnapshot(ExecutionProgressSnapshot snapshot)
    {
        if (snapshot.TotalCount <= 0)
        {
            HideExecutionProgressCore();
            return;
        }

        var normalizedCompleted = Math.Clamp(snapshot.CompletedCount, 0, snapshot.TotalCount);
        ExecutionProgressVisibility = Visibility.Visible;
        ExecutionProgressSummaryText = FormatLocalizedText(
            "mainWindow.progress.summary",
            $"已完成 {normalizedCompleted} / {snapshot.TotalCount}",
            ("completed", normalizedCompleted),
            ("total", snapshot.TotalCount));

        if (snapshot.CurrentItem is null)
        {
            ExecutionProgressCurrentItemText = normalizedCompleted >= snapshot.TotalCount
                ? GetLocalizedText("mainWindow.progress.currentItem.completed", "当前文件：已完成")
                : GetLocalizedText("mainWindow.progress.currentItem.preparing", "当前文件：正在准备...");
            ExecutionProgressValue = Math.Round((normalizedCompleted / (double)snapshot.TotalCount) * 100d, 1);

            if (normalizedCompleted >= snapshot.TotalCount)
            {
                IsExecutionProgressIndeterminate = false;
                ExecutionProgressPercentText = "100%";
                ExecutionProgressDetailText = snapshot.DetailOverride?.ResolveMain(this)
                    ?? GetLocalizedText("mainWindow.progress.detail.organizingCompleted", "处理完成，正在整理结果...");
                return;
            }

            IsExecutionProgressIndeterminate = true;
            ExecutionProgressPercentText = GetLocalizedText("mainWindow.progress.percent.preparing", "准备中");
            ExecutionProgressDetailText = snapshot.DetailOverride?.ResolveMain(this)
                ?? GetLocalizedText("mainWindow.progress.detail.preparing", "正在准备处理任务...");
            return;
        }

        ExecutionProgressCurrentItemText = FormatLocalizedText(
            "mainWindow.progress.currentItem.active",
            $"当前文件：{snapshot.CurrentItem.InputFileName}",
            ("fileName", snapshot.CurrentItem.InputFileName));

        if (snapshot.CurrentItemProgressRatio is double progressRatio)
        {
            var normalizedRatio = Math.Clamp(progressRatio, 0d, 1d);
            var overallProgress = (normalizedCompleted + normalizedRatio) / snapshot.TotalCount;
            IsExecutionProgressIndeterminate = false;
            ExecutionProgressValue = Math.Round(overallProgress * 100d, 1);
            ExecutionProgressPercentText = $"{Math.Round(normalizedRatio * 100d):0}%";
            ExecutionProgressDetailText = snapshot.DetailOverride?.ResolveMain(this)
                ?? CreateExecutionProgressDetail(snapshot.ProcessedDuration, snapshot.TotalDuration, normalizedRatio);
            snapshot.CurrentItem.UpdateRunningDetail(
                snapshot.DetailOverride?.ResolveRunning(this)
                ?? CreateRunningItemProgressDetail(snapshot.ProcessedDuration, snapshot.TotalDuration, normalizedRatio));
            return;
        }

        IsExecutionProgressIndeterminate = true;
        ExecutionProgressValue = Math.Round((normalizedCompleted / (double)snapshot.TotalCount) * 100d, 1);
        ExecutionProgressPercentText = GetLocalizedText("mainWindow.progress.percent.processing", "处理中");
        ExecutionProgressDetailText = snapshot.DetailOverride?.ResolveMain(this)
            ?? GetLocalizedText("mainWindow.progress.detail.processingNoEstimate", "正在处理当前文件，暂时无法估算总进度。");
        snapshot.CurrentItem.UpdateRunningDetail(
            snapshot.DetailOverride?.ResolveRunning(this)
            ?? GetLocalizedText("mainWindow.progress.itemDetail.processingNoEstimate", "正在处理，暂时无法估算总进度。"));
    }

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

    private string CreateExecutionProgressDetail(
        TimeSpan? processedDuration,
        TimeSpan? totalDuration,
        double progressRatio)
    {
        var percentText = $"{Math.Round(Math.Clamp(progressRatio, 0d, 1d) * 100d):0}%";

        if (processedDuration is { } processed && totalDuration is { } total && total > TimeSpan.Zero)
        {
            return FormatLocalizedText(
                "mainWindow.progress.detail.processedTotal",
                $"当前文件进度 {percentText} · 已处理 {FormatClockDuration(processed)} / {FormatClockDuration(total)}",
                ("percent", percentText),
                ("processed", FormatClockDuration(processed)),
                ("total", FormatClockDuration(total)));
        }

        if (processedDuration is { } processedOnly)
        {
            return FormatLocalizedText(
                "mainWindow.progress.detail.processedOnly",
                $"当前文件进度 {percentText} · 已处理 {FormatClockDuration(processedOnly)}",
                ("percent", percentText),
                ("processed", FormatClockDuration(processedOnly)));
        }

        return FormatLocalizedText(
            "mainWindow.progress.detail.percentOnly",
            $"当前文件进度 {percentText}",
            ("percent", percentText));
    }

    private string CreateRunningItemProgressDetail(
        TimeSpan? processedDuration,
        TimeSpan? totalDuration,
        double progressRatio)
    {
        var percentText = $"{Math.Round(Math.Clamp(progressRatio, 0d, 1d) * 100d):0}%";

        if (processedDuration is { } processed && totalDuration is { } total && total > TimeSpan.Zero)
        {
            return FormatLocalizedText(
                "mainWindow.progress.itemDetail.processedTotal",
                $"已处理 {FormatClockDuration(processed)} / {FormatClockDuration(total)}（{percentText}）",
                ("processed", FormatClockDuration(processed)),
                ("total", FormatClockDuration(total)),
                ("percent", percentText));
        }

        if (processedDuration is { } processedOnly)
        {
            return FormatLocalizedText(
                "mainWindow.progress.itemDetail.processedOnly",
                $"已处理 {FormatClockDuration(processedOnly)}（{percentText}）",
                ("processed", FormatClockDuration(processedOnly)),
                ("percent", percentText));
        }

        return FormatLocalizedText(
            "mainWindow.progress.itemDetail.percentOnly",
            $"正在处理（{percentText}）",
            ("percent", percentText));
    }

    private static string FormatClockDuration(TimeSpan duration) =>
        duration.TotalHours >= 1
            ? duration.ToString(@"hh\:mm\:ss")
            : duration.ToString(@"mm\:ss");

    private sealed class ExecutionProgressSnapshot
    {
        public static readonly ExecutionProgressSnapshot Hidden = new(0, 0, null, null, null, null, null);

        public ExecutionProgressSnapshot(
            int totalCount,
            int completedCount,
            MediaJobViewModel? currentItem,
            double? currentItemProgressRatio,
            TimeSpan? processedDuration,
            TimeSpan? totalDuration,
            LocalizedProgressText? detailOverride)
        {
            TotalCount = totalCount;
            CompletedCount = completedCount;
            CurrentItem = currentItem;
            CurrentItemProgressRatio = currentItemProgressRatio;
            ProcessedDuration = processedDuration;
            TotalDuration = totalDuration;
            DetailOverride = detailOverride;
        }

        public int TotalCount { get; }

        public int CompletedCount { get; }

        public MediaJobViewModel? CurrentItem { get; }

        public double? CurrentItemProgressRatio { get; }

        public TimeSpan? ProcessedDuration { get; }

        public TimeSpan? TotalDuration { get; }

        public LocalizedProgressText? DetailOverride { get; }
    }

    private sealed class LocalizedProgressText
    {
        private LocalizedProgressText(
            string mainKey,
            string mainFallback,
            string? runningKey,
            string? runningFallback)
        {
            MainKey = mainKey;
            MainFallback = mainFallback;
            RunningKey = runningKey;
            RunningFallback = runningFallback;
        }

        public string MainKey { get; }

        public string MainFallback { get; }

        public string? RunningKey { get; }

        public string? RunningFallback { get; }

        public static LocalizedProgressText Create(
            string mainKey,
            string mainFallback,
            string? runningKey = null,
            string? runningFallback = null) =>
            new(mainKey, mainFallback, runningKey, runningFallback);

        public string ResolveMain(MainViewModel owner) =>
            owner.GetLocalizedText(MainKey, MainFallback);

        public string ResolveRunning(MainViewModel owner) =>
            owner.GetLocalizedText(RunningKey ?? MainKey, RunningFallback ?? MainFallback);
    }
}
