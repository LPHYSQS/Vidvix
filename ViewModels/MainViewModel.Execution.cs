// 功能：主工作区执行编排（把 ViewModel 状态与媒体处理工作流服务连接起来）
// 模块：视频转换模块 / 音频转换模块
// 说明：可复用，负责 UI 状态编排与日志回写，不直接操作底层 FFmpeg。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Vidvix.Core.Models;

namespace Vidvix.ViewModels;

public sealed partial class MainViewModel
{
    // 运行时准备、预检和批处理执行逻辑集中在这里。

    private async Task ExecuteProcessingAsync()
    {
        var executionWorkspaceKind = _selectedWorkspaceKind;
        var executionItems = GetImportItems(executionWorkspaceKind);

        if (executionItems.Count == 0)
        {
            var emptyQueueMessage = GetEmptyQueueProcessingMessage();
            StatusMessage = emptyQueueMessage;
            AddUiLog(executionWorkspaceKind, LogLevel.Warning, emptyQueueMessage, clearExisting: false);
            return;
        }

        if (!await EnsureRuntimeReadyAsync(logUiFailure: true))
        {
            return;
        }

        var executionContext = new MediaProcessingContext(
            executionWorkspaceKind,
            executionWorkspaceKind == ProcessingWorkspaceKind.Audio ? ProcessingMode.AudioTrackExtract : SelectedProcessingMode.Mode,
            SelectedOutputFormat,
            SelectedTranscodingModeOption.Mode,
            EnableGpuAccelerationForTranscoding,
            VideoAccelerationKind.None);
        _executionCancellationSource?.Dispose();
        _executionCancellationSource = new CancellationTokenSource();

        var batchStartedAt = DateTimeOffset.UtcNow;

        try
        {
            IsSettingsPaneOpen = false;
            IsBusy = true;
            EnsureOutputDirectoryExists();
            RecalculatePlannedOutputs();
            ClearUiLogs(executionWorkspaceKind);

            foreach (var item in executionItems)
            {
                item.ResetStatus();
            }

            var standbyWorkspaceKind = executionWorkspaceKind == ProcessingWorkspaceKind.Audio
                ? ProcessingWorkspaceKind.Video
                : ProcessingWorkspaceKind.Audio;
            var standbyItemCount = GetImportItems(standbyWorkspaceKind).Count;
            if (standbyItemCount > 0)
            {
                AddUiLog(
                    executionWorkspaceKind,
                    LogLevel.Info,
                    CreateStandbyWorkspaceRetainedMessage(executionWorkspaceKind, executionItems.Count, standbyItemCount),
                    clearExisting: false);
            }

            executionContext = await ResolveExecutionContextAsync(executionContext, _executionCancellationSource.Token);
            var preflightFailedCount = await ValidateProcessingPreconditionsAsync(executionContext, executionItems, _executionCancellationSource.Token);
            var processableCount = executionItems.Count(item => item.IsPending);
            var totalProcessableCount = processableCount;

            var successCount = 0;
            var failedCount = preflightFailedCount;
            var cancelledCount = 0;
            var completedProcessCount = 0;
            string? lastSuccessfulOutputPath = null;

            if (processableCount == 0)
            {
                StatusMessage = CreateNoExecutableMessage(preflightFailedCount);
                AddUiLog(executionWorkspaceKind, LogLevel.Warning, StatusMessage, clearExisting: false);
                return;
            }

            StatusMessage = CreateProcessingStartedMessage(executionWorkspaceKind, executionItems.Count);
            AddUiLog(executionWorkspaceKind, LogLevel.Info, StatusMessage, clearExisting: false);

            if (preflightFailedCount > 0)
            {
                StatusMessage = CreateProcessingStartedAfterSkipMessage(processableCount, preflightFailedCount);
                AddUiLog(executionWorkspaceKind, LogLevel.Warning, StatusMessage, clearExisting: false);
            }

            ShowExecutionPreparationProgress(totalProcessableCount);

            for (var index = 0; index < executionItems.Count; index++)
            {
                var item = executionItems[index];

                if (!item.IsPending)
                {
                    continue;
                }

                if (_executionCancellationSource.IsCancellationRequested)
                {
                    item.MarkCancelled();
                    cancelledCount++;
                    continue;
                }

                var completedBeforeCurrent = completedProcessCount;
                var currentItemOrdinal = completedBeforeCurrent + 1;
                var inputDuration = await TryGetMediaDurationAsync(item.InputPath, _executionCancellationSource.Token);

                item.MarkRunning(
                    inputDuration.HasValue
                        ? CreateRunningItemProgressDetail(TimeSpan.Zero, inputDuration.Value, 0d)
                        : GetLocalizedText("mainWindow.progress.itemDetail.processingNoEstimate", "正在处理，暂时无法估算总进度。"));
                StatusMessage = CreateProcessingCurrentItemMessage(currentItemOrdinal, totalProcessableCount, item.InputFileName);
                UpdateExecutionProgress(
                    totalProcessableCount,
                    completedBeforeCurrent,
                    item,
                    inputDuration is null ? null : 0d,
                    TimeSpan.Zero,
                    inputDuration,
                    inputDuration is null
                        ? LocalizedProgressText.Create(
                            "mainWindow.progress.detail.processingNoEstimate",
                            "正在处理当前文件，暂时无法估算总进度。",
                            "mainWindow.progress.itemDetail.processingNoEstimate",
                            "正在处理，暂时无法估算总进度。")
                        : LocalizedProgressText.Create(
                            "mainWindow.progress.detail.waitingRealtime",
                            "等待 FFmpeg 返回实时进度...",
                            "mainWindow.progress.detail.waitingRealtime",
                            "等待 FFmpeg 返回实时进度..."));

                var progressReporter = new Progress<FFmpegProgressUpdate>(update =>
                    UpdateExecutionProgress(
                        totalProcessableCount,
                        completedBeforeCurrent,
                        item,
                        update.ProgressRatio,
                        update.ProcessedDuration,
                        update.TotalDuration ?? inputDuration,
                        update.ProgressRatio is null && !update.IsCompleted
                            ? LocalizedProgressText.Create(
                                "mainWindow.progress.detail.processingNoEstimate",
                                "正在处理当前文件，暂时无法估算总进度。",
                                "mainWindow.progress.itemDetail.processingNoEstimate",
                                "正在处理，暂时无法估算总进度。")
                            : null));

                var executionResult = await _mediaProcessingWorkflowService.ExecuteAsync(
                    new MediaProcessingItemExecutionRequest(
                        _runtimeExecutablePath!,
                        item.InputPath,
                        item.PlannedOutputPath,
                        executionContext)
                    {
                        InputDuration = inputDuration,
                        Progress = progressReporter,
                        OnCpuFallback = () =>
                        {
                            AddUiLog(
                                executionWorkspaceKind,
                                LogLevel.Warning,
                                CreateGpuFallbackMessage(item.InputFileName),
                                clearExisting: false);

                            UpdateExecutionProgress(
                                totalProcessableCount,
                                completedBeforeCurrent,
                                item,
                                inputDuration is null ? null : 0d,
                                TimeSpan.Zero,
                                inputDuration,
                                inputDuration is null
                                    ? LocalizedProgressText.Create(
                                        "mainWindow.progress.detail.cpuFallbackRetryNoEstimate",
                                        "已回退为 CPU，正在重新尝试并估算进度...",
                                        "mainWindow.progress.detail.cpuFallbackRetryNoEstimate",
                                        "已回退为 CPU，正在重新尝试并估算进度...")
                                    : LocalizedProgressText.Create(
                                        "mainWindow.progress.detail.cpuFallbackRetry",
                                        "已回退为 CPU，正在重新尝试...",
                                        "mainWindow.progress.detail.cpuFallbackRetry",
                                        "已回退为 CPU，正在重新尝试..."));
                        }
                    },
                    _executionCancellationSource.Token);

                var result = executionResult.ExecutionResult;
                var usedCpuFallback = executionResult.UsedCpuFallback;
                var elapsedText = FormatDuration(executionResult.TotalDuration);

                if (result.WasSuccessful && File.Exists(item.PlannedOutputPath))
                {
                    item.MarkSucceeded(CreateElapsedDetail(elapsedText, usedCpuFallback));
                    successCount++;
                    completedProcessCount++;
                    lastSuccessfulOutputPath = item.PlannedOutputPath;
                    AddUiLog(
                        executionWorkspaceKind,
                        LogLevel.Info,
                        CreateSucceededLogMessage(item.InputFileName, elapsedText, usedCpuFallback),
                        clearExisting: false);
                    continue;
                }

                if (result.WasCancelled)
                {
                    item.MarkCancelled();
                    cancelledCount++;
                    cancelledCount += MarkRemainingItemsCancelled(executionItems, index + 1);
                    AddUiLog(executionWorkspaceKind, LogLevel.Warning, GetProcessingCancelledPendingMessage(), clearExisting: false);
                    break;
                }

                var failureMessage = CreateFriendlyFailureMessage(result, executionContext);
                item.MarkFailed(CreateReasonDetail(failureMessage));
                failedCount++;
                completedProcessCount++;
                AddUiLog(executionWorkspaceKind, LogLevel.Error, CreateFailedLogMessage(item.InputFileName, elapsedText, failureMessage), clearExisting: false);
            }

            var wasCancelled = _executionCancellationSource.IsCancellationRequested;
            UpdateExecutionProgress(
                totalProcessableCount,
                wasCancelled ? completedProcessCount : totalProcessableCount,
                currentItem: null,
                currentItemProgressRatio: null,
                processedDuration: null,
                totalDuration: null,
                detailOverride: wasCancelled
                    ? LocalizedProgressText.Create(
                        "mainWindow.progress.detail.organizingCancelled",
                        "任务已取消，正在整理当前结果...")
                    : LocalizedProgressText.Create(
                        "mainWindow.progress.detail.organizingCompleted",
                        "处理完成，正在整理结果..."));

            StatusMessage = wasCancelled
                ? CreateCancelledSummaryMessage(successCount, failedCount, cancelledCount)
                : CreateCompletedSummaryMessage(successCount, failedCount);

            AddUiLog(
                executionWorkspaceKind,
                wasCancelled || failedCount > 0 ? LogLevel.Warning : LogLevel.Info,
                CreateBatchSummaryMessage(successCount, failedCount, cancelledCount, DateTimeOffset.UtcNow - batchStartedAt, wasCancelled),
                clearExisting: false);

            RevealLastSuccessfulOutputIfNeeded(lastSuccessfulOutputPath, successCount, wasCancelled);
        }
        catch (OperationCanceledException) when (_executionCancellationSource?.IsCancellationRequested == true)
        {
            var cancelledCount = MarkRemainingItemsCancelled(executionItems, 0);
            StatusMessage = CreateCancelledCountStatusMessage(cancelledCount);
            AddUiLog(executionWorkspaceKind, LogLevel.Warning, GetProcessingCancelledPendingMessage(), clearExisting: false);
        }
        catch (Exception exception)
        {
            StatusMessage = GetProcessingUnexpectedErrorMessage();
            _logger.Log(LogLevel.Error, "批量处理流程被异常中断。", exception);
            AddUiLog(executionWorkspaceKind, LogLevel.Error, CreateProcessingInterruptedMessage(exception.Message), clearExisting: false);
        }
        finally
        {
            ResetExecutionProgress();
            IsBusy = false;
            _executionCancellationSource?.Dispose();
            _executionCancellationSource = null;
        }
    }

    private void CancelExecution()
    {
        if (!IsBusy)
        {
            return;
        }

        StatusMessage = GetProcessingCancellingMessage();
        _executionCancellationSource?.Cancel();
    }

    private bool CanExecuteProcessing() =>
        !IsBusy &&
        !DetailPanel.IsOpen &&
        ImportItems.Count > 0 &&
        AvailableOutputFormats.Count > 0;

    private async Task<int> ValidateProcessingPreconditionsAsync(
        MediaProcessingContext executionContext,
        IReadOnlyList<MediaJobViewModel> executionItems,
        CancellationToken cancellationToken)
    {
        var preflightResult = await _mediaProcessingWorkflowService.ValidatePreconditionsAsync(
            executionContext,
            executionItems.Select(item => item.InputPath).ToArray(),
            cancellationToken);

        foreach (var message in preflightResult.Messages)
        {
            AddUiLog(executionContext.WorkspaceKind, message.Level, message.Message, clearExisting: false);
        }

        var blockingIssuesByPath = preflightResult.BlockingIssues.ToDictionary(
            issue => issue.InputPath,
            issue => issue,
            StringComparer.OrdinalIgnoreCase);
        var failedCount = 0;

        foreach (var item in executionItems)
        {
            if (!blockingIssuesByPath.TryGetValue(item.InputPath, out var issue))
            {
                continue;
            }

            item.MarkFailed(CreateReasonDetail(issue.FailureMessage));
            failedCount++;
            AddUiLog(executionContext.WorkspaceKind, LogLevel.Warning, CreatePreflightBlockedLogMessage(item.InputFileName, issue.FailureMessage), clearExisting: false);
        }

        return failedCount;
    }

    private async Task<MediaProcessingContext> ResolveExecutionContextAsync(
        MediaProcessingContext executionContext,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_runtimeExecutablePath))
        {
            throw new InvalidOperationException("运行环境尚未准备完成。");
        }

        var resolutionResult = await _mediaProcessingWorkflowService.ResolveExecutionContextAsync(
            _runtimeExecutablePath,
            executionContext,
            cancellationToken);

        foreach (var message in resolutionResult.Messages)
        {
            AddUiLog(executionContext.WorkspaceKind, message.Level, message.Message, clearExisting: false);
        }

        return resolutionResult.Context;
    }

    private async Task<bool> EnsureRuntimeReadyAsync(CancellationToken cancellationToken = default, bool logUiFailure = false)
    {
        if (!string.IsNullOrWhiteSpace(_runtimeExecutablePath) && File.Exists(_runtimeExecutablePath))
        {
            return true;
        }

        try
        {
            IsBusy = true;
            StatusMessage = GetRuntimePreparingMessage();

            var resolution = await _mediaProcessingWorkflowService.EnsureRuntimeReadyAsync(cancellationToken);
            _runtimeExecutablePath = resolution.ExecutablePath;

            SetReadyStatusMessage();
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            StatusMessage = GetRuntimePreparationCancelledMessage();

            if (logUiFailure)
            {
                AddUiLog(LogLevel.Warning, StatusMessage, clearExisting: false);
            }

            return false;
        }
        catch (Exception exception)
        {
            StatusMessage = GetRuntimePreparationFailedMessage();
            _logger.Log(LogLevel.Error, "准备本地 FFmpeg 时发生异常。", exception);

            if (logUiFailure)
            {
                var reason = ExtractFriendlyExceptionMessage(exception);
                AddUiLog(LogLevel.Error, CreateRuntimeNotReadyLogMessage(reason), clearExisting: false);
            }

            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }
}
