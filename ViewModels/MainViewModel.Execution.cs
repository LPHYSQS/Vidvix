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
                    $"本次仅处理当前{GetMediaLabel(executionWorkspaceKind)}模块中的 {executionItems.Count} 个文件；另一模块暂存的 {standbyItemCount} 个文件会保留，不会参与本次处理。",
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
                StatusMessage = preflightFailedCount == 1
                    ? "当前队列中没有可执行的文件，请检查媒体轨道后重试。"
                    : "当前队列中的文件都不满足所选处理模式，未执行处理。";
                AddUiLog(executionWorkspaceKind, LogLevel.Warning, StatusMessage, clearExisting: false);
                return;
            }

            StatusMessage = $"开始处理当前{GetMediaLabel(executionWorkspaceKind)}模块中的 {executionItems.Count} 个文件...";
            AddUiLog(executionWorkspaceKind, LogLevel.Info, $"开始处理当前{GetMediaLabel(executionWorkspaceKind)}模块中的 {executionItems.Count} 个文件。", clearExisting: false);

            if (preflightFailedCount > 0)
            {
                StatusMessage = $"开始处理 {processableCount} 个文件，已提前跳过 {preflightFailedCount} 个不符合当前模式的文件。";
                AddUiLog(executionWorkspaceKind, LogLevel.Warning, $"开始处理 {processableCount} 个文件，已提前跳过 {preflightFailedCount} 个不符合当前模式的文件。", clearExisting: false);
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
                        : "正在处理，正在估算总进度...");
                StatusMessage = $"正在处理第 {currentItemOrdinal} / {totalProcessableCount} 个文件：{item.InputFileName}";
                UpdateExecutionProgress(
                    totalProcessableCount,
                    completedBeforeCurrent,
                    item,
                    inputDuration is null ? null : 0d,
                    TimeSpan.Zero,
                    inputDuration,
                    inputDuration is null
                        ? "正在处理当前文件，暂时无法估算总进度。"
                        : "等待 FFmpeg 返回实时进度...");

                var progressReporter = new Progress<FFmpegProgressUpdate>(update =>
                    UpdateExecutionProgress(
                        totalProcessableCount,
                        completedBeforeCurrent,
                        item,
                        update.ProgressRatio,
                        update.ProcessedDuration,
                        update.TotalDuration ?? inputDuration,
                        update.ProgressRatio is null && !update.IsCompleted
                            ? "正在处理当前文件，暂时无法估算总进度。"
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
                                $"{item.InputFileName} 的 GPU 转码未成功，已自动回退为 CPU 重新尝试一次。",
                                clearExisting: false);

                            UpdateExecutionProgress(
                                totalProcessableCount,
                                completedBeforeCurrent,
                                item,
                                inputDuration is null ? null : 0d,
                                TimeSpan.Zero,
                                inputDuration,
                                inputDuration is null
                                    ? "已回退为 CPU，正在重新尝试并估算进度。"
                                    : "已回退为 CPU，正在重新尝试...");
                        }
                    },
                    _executionCancellationSource.Token);

                var result = executionResult.ExecutionResult;
                var usedCpuFallback = executionResult.UsedCpuFallback;
                var elapsedText = FormatDuration(executionResult.TotalDuration);

                if (result.WasSuccessful && File.Exists(item.PlannedOutputPath))
                {
                    item.MarkSucceeded(usedCpuFallback ? $"用时 {elapsedText}（已自动回退 CPU）" : $"用时 {elapsedText}");
                    successCount++;
                    completedProcessCount++;
                    lastSuccessfulOutputPath = item.PlannedOutputPath;
                    AddUiLog(
                        executionWorkspaceKind,
                        LogLevel.Info,
                        usedCpuFallback
                            ? $"{item.InputFileName} 已在回退到 CPU 后处理成功，用时 {elapsedText}。"
                            : $"{item.InputFileName} 处理成功，用时 {elapsedText}。",
                        clearExisting: false);
                    continue;
                }

                if (result.WasCancelled)
                {
                    item.MarkCancelled();
                    cancelledCount++;
                    cancelledCount += MarkRemainingItemsCancelled(executionItems, index + 1);
                    AddUiLog(executionWorkspaceKind, LogLevel.Warning, "任务已取消，未完成的文件已停止处理。", clearExisting: false);
                    break;
                }

                var failureMessage = CreateFriendlyFailureMessage(result, executionContext);
                item.MarkFailed($"原因：{failureMessage}");
                failedCount++;
                completedProcessCount++;
                AddUiLog(executionWorkspaceKind, LogLevel.Error, $"{item.InputFileName} 处理失败，用时 {elapsedText}。原因：{failureMessage}", clearExisting: false);
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
                    ? "任务已取消，正在整理当前结果..."
                    : "处理完成，正在整理结果...");

            StatusMessage = wasCancelled
                ? $"任务已取消，成功 {successCount} 个，失败 {failedCount} 个，取消 {cancelledCount} 个。"
                : failedCount == 0
                    ? $"全部处理完成，共成功 {successCount} 个文件。"
                    : $"处理完成，成功 {successCount} 个，失败 {failedCount} 个。";

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
            StatusMessage = cancelledCount > 0
                ? $"任务已取消，已取消 {cancelledCount} 个未完成文件。"
                : "任务已取消。";
            AddUiLog(executionWorkspaceKind, LogLevel.Warning, "任务已取消，未完成的文件已停止处理。", clearExisting: false);
        }
        catch (Exception exception)
        {
            StatusMessage = "处理过程中发生异常。";
            _logger.Log(LogLevel.Error, "批量处理流程被异常中断。", exception);
            AddUiLog(executionWorkspaceKind, LogLevel.Error, $"批量处理被中断。原因：{exception.Message}", clearExisting: false);
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

        StatusMessage = "正在取消当前任务...";
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

            item.MarkFailed($"原因：{issue.FailureMessage}");
            failedCount++;
            AddUiLog(executionContext.WorkspaceKind, LogLevel.Warning, $"{item.InputFileName} {issue.FailureMessage}", clearExisting: false);
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
            StatusMessage = RuntimePreparingMessage;

            var resolution = await _mediaProcessingWorkflowService.EnsureRuntimeReadyAsync(cancellationToken);
            _runtimeExecutablePath = resolution.ExecutablePath;

            SetReadyStatusMessage();
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            StatusMessage = RuntimePreparationCancelledMessage;

            if (logUiFailure)
            {
                AddUiLog(LogLevel.Warning, RuntimePreparationCancelledMessage, clearExisting: false);
            }

            return false;
        }
        catch (Exception exception)
        {
            StatusMessage = RuntimePreparationFailedMessage;
            _logger.Log(LogLevel.Error, "准备本地 FFmpeg 时发生异常。", exception);

            if (logUiFailure)
            {
                var reason = ExtractFriendlyExceptionMessage(exception);
                AddUiLog(LogLevel.Error, $"运行环境未就绪，无法开始处理。原因：{reason}", clearExisting: false);
            }

            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }
}
