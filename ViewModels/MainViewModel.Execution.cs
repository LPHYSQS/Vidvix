using System;
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
        if (ImportItems.Count == 0)
        {
            StatusMessage = GetEmptyQueueProcessingMessage();
            AddUiLog(LogLevel.Warning, GetEmptyQueueProcessingMessage(), clearExisting: false);
            return;
        }

        if (!await EnsureRuntimeReadyAsync(logUiFailure: true))
        {
            return;
        }

        _executionCancellationSource?.Dispose();
        _executionCancellationSource = new CancellationTokenSource();

        var batchStartedAt = DateTimeOffset.UtcNow;

        try
        {
            IsSettingsPaneOpen = false;
            IsBusy = true;
            EnsureOutputDirectoryExists();
            RecalculatePlannedOutputs();
            ClearUiLogs();

            foreach (var item in ImportItems)
            {
                item.ResetStatus();
            }

            var preflightFailedCount = await ValidateProcessingPreconditionsAsync(_executionCancellationSource.Token);
            var processableCount = ImportItems.Count(item => item.IsPending);

            var successCount = 0;
            var failedCount = preflightFailedCount;
            var cancelledCount = 0;
            string? lastSuccessfulOutputPath = null;

            if (processableCount == 0)
            {
                StatusMessage = preflightFailedCount == 1
                    ? "当前队列中没有可执行的文件，请检查媒体轨道后重试。"
                    : "当前队列中的文件都不满足所选处理模式，未执行处理。";
                AddUiLog(LogLevel.Warning, StatusMessage, clearExisting: false);
                return;
            }

            StatusMessage = $"开始处理 {ImportItems.Count} 个文件...";
            AddUiLog(LogLevel.Info, $"开始处理 {ImportItems.Count} 个文件。", clearExisting: false);

            if (preflightFailedCount > 0)
            {
                StatusMessage = $"开始处理 {processableCount} 个文件，已提前跳过 {preflightFailedCount} 个不符合当前模式的文件。";
                AddUiLog(LogLevel.Warning, $"开始处理 {processableCount} 个文件，已提前跳过 {preflightFailedCount} 个不符合当前模式的文件。", clearExisting: false);
            }

            for (var index = 0; index < ImportItems.Count; index++)
            {
                var item = ImportItems[index];

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

                item.MarkRunning();

                var command = BuildCommand(item.InputPath, item.PlannedOutputPath);
                var executionOptions = new FFmpegExecutionOptions
                {
                    Timeout = _configuration.DefaultExecutionTimeout
                };

                var result = await _ffmpegService.ExecuteAsync(
                    command,
                    executionOptions,
                    _executionCancellationSource.Token);

                var elapsedText = FormatDuration(result.Duration);

                if (result.WasSuccessful && File.Exists(item.PlannedOutputPath))
                {
                    item.MarkSucceeded($"用时 {elapsedText}");
                    successCount++;
                    lastSuccessfulOutputPath = item.PlannedOutputPath;
                    AddUiLog(LogLevel.Info, $"{item.InputFileName} 处理成功，用时 {elapsedText}。", clearExisting: false);
                    continue;
                }

                if (result.WasCancelled)
                {
                    item.MarkCancelled();
                    cancelledCount++;
                    cancelledCount += MarkRemainingItemsCancelled(index + 1);
                    AddUiLog(LogLevel.Warning, "任务已取消，未完成的文件已停止处理。", clearExisting: false);
                    break;
                }

                var failureMessage = CreateFriendlyFailureMessage(result);
                item.MarkFailed($"原因：{failureMessage}");
                failedCount++;
                AddUiLog(LogLevel.Error, $"{item.InputFileName} 处理失败，用时 {elapsedText}。原因：{failureMessage}", clearExisting: false);
            }

            var wasCancelled = _executionCancellationSource.IsCancellationRequested;

            StatusMessage = wasCancelled
                ? $"任务已取消，成功 {successCount} 个，失败 {failedCount} 个，取消 {cancelledCount} 个。"
                : failedCount == 0
                    ? $"全部处理完成，共成功 {successCount} 个文件。"
                    : $"处理完成，成功 {successCount} 个，失败 {failedCount} 个。";

            AddUiLog(
                wasCancelled || failedCount > 0 ? LogLevel.Warning : LogLevel.Info,
                CreateBatchSummaryMessage(successCount, failedCount, cancelledCount, DateTimeOffset.UtcNow - batchStartedAt, wasCancelled),
                clearExisting: false);

            RevealLastSuccessfulOutputIfNeeded(lastSuccessfulOutputPath, successCount, wasCancelled);
        }
        catch (OperationCanceledException) when (_executionCancellationSource?.IsCancellationRequested == true)
        {
            var cancelledCount = MarkRemainingItemsCancelled(0);
            StatusMessage = cancelledCount > 0
                ? $"任务已取消，已取消 {cancelledCount} 个未完成文件。"
                : "任务已取消。";
            AddUiLog(LogLevel.Warning, "任务已取消，未完成的文件已停止处理。", clearExisting: false);
        }
        catch (Exception exception)
        {
            StatusMessage = "处理过程中发生异常。";
            _logger.Log(LogLevel.Error, "批量处理流程被异常中断。", exception);
            AddUiLog(LogLevel.Error, $"批量处理被中断。原因：{exception.Message}", clearExisting: false);
        }
        finally
        {
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

    private async Task<int> ValidateProcessingPreconditionsAsync(CancellationToken cancellationToken)
    {
        if (!TryGetRequiredTrackType(out var requiredTrackType))
        {
            return 0;
        }

        var failedCount = 0;

        foreach (var item in ImportItems)
        {
            cancellationToken.ThrowIfCancellationRequested();

            MediaDetailsSnapshot? snapshot = null;
            if (_mediaInfoService.TryGetCachedDetails(item.InputPath, out var cachedSnapshot))
            {
                snapshot = cachedSnapshot;
            }
            else
            {
                var result = await _mediaInfoService.GetMediaDetailsAsync(item.InputPath, cancellationToken);
                if (!result.IsSuccess || result.Snapshot is null)
                {
                    var reason = result.ErrorMessage ?? "无法提前检测该文件的媒体轨道。";
                    _logger.Log(LogLevel.Warning, $"处理前预检失败，将继续尝试处理：{item.InputPath}，原因：{reason}");
                    AddUiLog(LogLevel.Warning, $"{item.InputFileName} 未能完成轨道预检，将继续尝试处理。原因：{reason}", clearExisting: false);
                    continue;
                }

                snapshot = result.Snapshot;
            }

            if (HasRequiredTrack(snapshot, requiredTrackType))
            {
                continue;
            }

            var failureMessage = CreateMissingRequiredTrackMessage(requiredTrackType);
            item.MarkFailed($"原因：{failureMessage}");
            failedCount++;
            AddUiLog(LogLevel.Warning, $"{item.InputFileName} {failureMessage}", clearExisting: false);
        }

        return failedCount;
    }

    private bool TryGetRequiredTrackType(out RequiredTrackType requiredTrackType)
    {
        if (IsAudioWorkspace)
        {
            requiredTrackType = RequiredTrackType.Audio;
            return true;
        }

        switch (SelectedProcessingMode.Mode)
        {
            case ProcessingMode.VideoTrackExtract:
                requiredTrackType = RequiredTrackType.Video;
                return true;
            case ProcessingMode.AudioTrackExtract:
                requiredTrackType = RequiredTrackType.Audio;
                return true;
            default:
                requiredTrackType = default;
                return false;
        }
    }

    private static bool HasRequiredTrack(MediaDetailsSnapshot snapshot, RequiredTrackType requiredTrackType) =>
        requiredTrackType switch
        {
            RequiredTrackType.Video => snapshot.HasVideoStream,
            RequiredTrackType.Audio => snapshot.HasAudioStream,
            _ => false
        };

    private string CreateMissingRequiredTrackMessage(RequiredTrackType requiredTrackType)
    {
        var outputFormatName = SelectedOutputFormat.DisplayName;

        if (IsAudioWorkspace)
        {
            return $"未检测到音频流，无法转换为 {outputFormatName}。";
        }

        return requiredTrackType switch
        {
            RequiredTrackType.Video => $"未检测到视频轨道，无法提取为 {outputFormatName}。",
            RequiredTrackType.Audio => $"未检测到音频轨道，无法提取为 {outputFormatName}。",
            _ => "当前文件不满足所选处理模式。"
        };
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

            var resolution = await _ffmpegRuntimeService.EnsureAvailableAsync(cancellationToken);
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

    private enum RequiredTrackType
    {
        Video,
        Audio
    }
}
