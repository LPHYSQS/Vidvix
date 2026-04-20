using System;
using Vidvix.Core.Models;

namespace Vidvix.ViewModels;

public sealed partial class MainViewModel
{
    private string GetRuntimePreparingMessage() =>
        GetLocalizedText("mainWindow.message.runtimePreparing", RuntimePreparingMessageFallback);

    private string GetReadyForProcessingMessage() =>
        GetLocalizedText("mainWindow.message.readyForProcessing", ReadyForProcessingMessageFallback);

    private string GetRuntimePreparationCancelledMessage() =>
        GetLocalizedText("mainWindow.message.runtimePreparationCancelled", RuntimePreparationCancelledMessageFallback);

    private string GetRuntimePreparationFailedMessage() =>
        GetLocalizedText("mainWindow.message.runtimePreparationFailed", RuntimePreparationFailedMessageFallback);

    private string CreateQueueSummaryText(int count) => count switch
    {
        <= 0 => GetLocalizedText("mainWindow.queue.summary.empty", "等待导入"),
        1 => GetLocalizedText("mainWindow.queue.summary.single", "1 个文件"),
        _ => FormatLocalizedText(
            "mainWindow.queue.summary.multiple",
            $"{count} 个文件",
            ("count", count))
    };

    private string CreateReasonDetail(string reason) =>
        FormatLocalizedText(
            "mainWindow.queue.item.status.reason",
            $"原因：{reason}",
            ("reason", reason));

    private string CreateElapsedDetail(string elapsedText, bool usedCpuFallback) =>
        usedCpuFallback
            ? FormatLocalizedText(
                "mainWindow.queue.item.status.elapsedCpuFallback",
                $"用时 {elapsedText}（已自动回退 CPU）",
                ("duration", elapsedText))
            : FormatLocalizedText(
                "mainWindow.queue.item.status.elapsed",
                $"用时 {elapsedText}",
                ("duration", elapsedText));

    private string CreateStandbyWorkspaceRetainedMessage(
        ProcessingWorkspaceKind executionWorkspaceKind,
        int executionCount,
        int standbyItemCount) =>
        FormatLocalizedText(
            "mainWindow.message.processingStandbyRetained",
            $"本次仅处理当前{GetMediaLabel(executionWorkspaceKind)}模块中的 {executionCount} 个文件；另一模块暂存的 {standbyItemCount} 个文件会保留，不会参与本次处理。",
            ("workspace", GetMediaLabel(executionWorkspaceKind)),
            ("count", executionCount),
            ("standbyCount", standbyItemCount));

    private string CreateNoExecutableMessage(int preflightFailedCount) =>
        preflightFailedCount == 1
            ? GetLocalizedText(
                "mainWindow.message.processingNoExecutableSingle",
                "当前队列中没有可执行的文件，请检查媒体轨道后重试。")
            : GetLocalizedText(
                "mainWindow.message.processingNoExecutableMultiple",
                "当前队列中的文件都不满足所选处理模式，未执行处理。");

    private string CreateProcessingStartedMessage(ProcessingWorkspaceKind workspaceKind, int count) =>
        FormatLocalizedText(
            "mainWindow.message.processingStarted",
            $"开始处理当前{GetMediaLabel(workspaceKind)}模块中的 {count} 个文件。",
            ("workspace", GetMediaLabel(workspaceKind)),
            ("count", count));

    private string CreateProcessingStartedAfterSkipMessage(int processableCount, int preflightFailedCount) =>
        FormatLocalizedText(
            "mainWindow.message.processingStartedAfterSkip",
            $"开始处理 {processableCount} 个文件，已提前跳过 {preflightFailedCount} 个不符合当前模式的文件。",
            ("processableCount", processableCount),
            ("skippedCount", preflightFailedCount));

    private string CreateProcessingCurrentItemMessage(int currentItemOrdinal, int totalProcessableCount, string fileName) =>
        FormatLocalizedText(
            "mainWindow.message.processingCurrentItem",
            $"正在处理第 {currentItemOrdinal} / {totalProcessableCount} 个文件：{fileName}",
            ("index", currentItemOrdinal),
            ("total", totalProcessableCount),
            ("fileName", fileName));

    private string CreateGpuFallbackMessage(string fileName) =>
        FormatLocalizedText(
            "mainWindow.message.processingItemGpuFallback",
            $"{fileName} 的 GPU 转码未成功，已自动回退为 CPU 重新尝试一次。",
            ("fileName", fileName));

    private string CreateSucceededLogMessage(string fileName, string elapsedText, bool usedCpuFallback) =>
        usedCpuFallback
            ? FormatLocalizedText(
                "mainWindow.message.processingItemSucceededCpuFallback",
                $"{fileName} 已在回退到 CPU 后处理成功，用时 {elapsedText}。",
                ("fileName", fileName),
                ("duration", elapsedText))
            : FormatLocalizedText(
                "mainWindow.message.processingItemSucceeded",
                $"{fileName} 处理成功，用时 {elapsedText}。",
                ("fileName", fileName),
                ("duration", elapsedText));

    private string GetProcessingCancelledPendingMessage() =>
        GetLocalizedText(
            "mainWindow.message.processingCancelledPending",
            "任务已取消，未完成的文件已停止处理。");

    private string CreateFailedLogMessage(string fileName, string elapsedText, string reason) =>
        FormatLocalizedText(
            "mainWindow.message.processingItemFailed",
            $"{fileName} 处理失败，用时 {elapsedText}。原因：{reason}",
            ("fileName", fileName),
            ("duration", elapsedText),
            ("reason", reason));

    private string CreateCancelledSummaryMessage(int successCount, int failedCount, int cancelledCount) =>
        FormatLocalizedText(
            "mainWindow.message.processingCancelledSummary",
            $"任务已取消，成功 {successCount} 个，失败 {failedCount} 个，取消 {cancelledCount} 个。",
            ("successCount", successCount),
            ("failedCount", failedCount),
            ("cancelledCount", cancelledCount));

    private string CreateCompletedSummaryMessage(int successCount, int failedCount) =>
        failedCount == 0
            ? FormatLocalizedText(
                "mainWindow.message.processingCompletedAll",
                $"全部处理完成，共成功 {successCount} 个文件。",
                ("successCount", successCount))
            : FormatLocalizedText(
                "mainWindow.message.processingCompletedPartial",
                $"处理完成，成功 {successCount} 个，失败 {failedCount} 个。",
                ("successCount", successCount),
                ("failedCount", failedCount));

    private string CreateCancelledCountStatusMessage(int cancelledCount) =>
        cancelledCount > 0
            ? FormatLocalizedText(
                "mainWindow.message.processingCancelledCount",
                $"任务已取消，已取消 {cancelledCount} 个未完成文件。",
                ("cancelledCount", cancelledCount))
            : GetLocalizedText(
                "mainWindow.message.processingCancelled",
                "任务已取消。");

    private string GetProcessingUnexpectedErrorMessage() =>
        GetLocalizedText("mainWindow.message.processingUnexpectedError", "处理过程中发生异常。");

    private string CreateProcessingInterruptedMessage(string reason) =>
        FormatLocalizedText(
            "mainWindow.message.processingInterrupted",
            $"批量处理被中断。原因：{reason}",
            ("reason", reason));

    private string GetProcessingCancellingMessage() =>
        GetLocalizedText("mainWindow.message.processingCancelling", "正在取消当前任务...");

    private string CreateRuntimeNotReadyLogMessage(string reason) =>
        FormatLocalizedText(
            "mainWindow.message.runtimeNotReady",
            $"运行环境未就绪，无法开始处理。原因：{reason}",
            ("reason", reason));

    private string CreatePreflightBlockedLogMessage(string fileName, string reason) =>
        FormatLocalizedText(
            "mainWindow.message.preflightBlocked",
            $"{fileName}：{reason}",
            ("fileName", fileName),
            ("reason", reason));

    private void RevealLastSuccessfulOutputIfNeeded(string? outputPath, int successCount, bool wasCancelled)
    {
        if (!RevealOutputFileAfterProcessing ||
            wasCancelled ||
            successCount == 0 ||
            string.IsNullOrWhiteSpace(outputPath))
        {
            return;
        }

        try
        {
            _fileRevealService.RevealFile(outputPath);
            AddUiLog(
                LogLevel.Info,
                successCount == 1
                    ? GetLocalizedText(
                        "mainWindow.message.outputRevealSingle",
                        "已打开输出文件所在文件夹，并选中处理完成的文件。")
                    : GetLocalizedText(
                        "mainWindow.message.outputRevealMultiple",
                        "已打开最后一个成功输出文件所在文件夹，并选中该输出文件。"),
                clearExisting: false);
        }
        catch (Exception exception)
        {
            _logger.Log(LogLevel.Warning, "打开输出文件所在位置失败。", exception);
            AddUiLog(
                LogLevel.Warning,
                FormatLocalizedText(
                    "mainWindow.message.outputRevealFailed",
                    $"处理已完成，但未能打开输出文件所在位置。原因：{ExtractFriendlyExceptionMessage(exception)}",
                    ("reason", ExtractFriendlyExceptionMessage(exception))),
                clearExisting: false);
        }
    }

    private string CreateFriendlyFailureMessage(FFmpegExecutionResult result, MediaProcessingContext executionContext)
    {
        var standardError = result.StandardError;

        if (result.TimedOut)
        {
            return GetLocalizedText("mainWindow.message.processingTimeout", "处理超时。");
        }

        if (standardError.Contains("matches no streams", StringComparison.OrdinalIgnoreCase))
        {
            if (executionContext.WorkspaceKind == ProcessingWorkspaceKind.Audio)
            {
                return GetLocalizedText("mainWindow.message.noAudioStream", "该文件没有可转换的音频流。");
            }

            return executionContext.ProcessingMode switch
            {
                ProcessingMode.VideoTrackExtract => GetLocalizedText("mainWindow.message.noVideoTrack", "该文件没有可提取的视频轨道。"),
                ProcessingMode.AudioTrackExtract => GetLocalizedText("mainWindow.message.noAudioTrack", "该文件没有可提取的音频轨道。"),
                ProcessingMode.SubtitleTrackExtract => GetLocalizedText("mainWindow.message.noSubtitleTrack", "该文件没有可提取的字幕轨道。"),
                _ => GetLocalizedText("mainWindow.message.noProcessableTrack", "该文件缺少可处理的媒体流。")
            };
        }

        if (standardError.Contains("Subtitle encoding currently only possible from text to text or bitmap to bitmap", StringComparison.OrdinalIgnoreCase))
        {
            return GetLocalizedText(
                "mainWindow.message.subtitleFormatIncompatible",
                "当前字幕轨道与目标格式不兼容：图形字幕不能直接转成文本字幕。请尝试导出为 MKS 保留原字幕轨道。");
        }

        if (standardError.Contains("not currently supported in container", StringComparison.OrdinalIgnoreCase) ||
            standardError.Contains("Could not write header", StringComparison.OrdinalIgnoreCase))
        {
            return GetLocalizedText(
                "mainWindow.message.outputFormatIncompatible",
                "目标格式与当前媒体流不兼容，请尝试 MP4、MKV、MOV 或更换其他导出格式。");
        }

        if (standardError.Contains("Permission denied", StringComparison.OrdinalIgnoreCase))
        {
            return GetLocalizedText("mainWindow.message.outputPermissionDenied", "输出文件正在被占用，或当前目录没有写入权限。");
        }

        if (standardError.Contains("Unknown encoder", StringComparison.OrdinalIgnoreCase))
        {
            return GetLocalizedText("mainWindow.message.outputEncoderMissing", "当前环境缺少所需格式支持，无法输出该格式。");
        }

        if (standardError.Contains("Invalid argument", StringComparison.OrdinalIgnoreCase))
        {
            return GetLocalizedText("mainWindow.message.outputInvalidArguments", "当前输出格式或参数无效，无法完成处理。");
        }

        if (standardError.Contains("No such file or directory", StringComparison.OrdinalIgnoreCase))
        {
            return GetLocalizedText("mainWindow.message.outputPathMissing", "输入文件不存在，或输出目录不可用。");
        }

        var extractedReason = TryExtractMeaningfulErrorLine(standardError);
        if (!string.IsNullOrWhiteSpace(extractedReason))
        {
            return extractedReason;
        }

        return result.FailureReason ?? GetLocalizedText("mainWindow.message.processingFailed", "处理失败。");
    }

    private static int MarkRemainingItemsCancelled(System.Collections.Generic.IReadOnlyList<MediaJobViewModel> executionItems, int startIndex)
    {
        var cancelledCount = 0;

        for (var index = startIndex; index < executionItems.Count; index++)
        {
            var item = executionItems[index];
            if (!item.IsPending)
            {
                continue;
            }

            item.MarkCancelled();
            cancelledCount++;
        }

        return cancelledCount;
    }

    private string CreateBatchSummaryMessage(
        int successCount,
        int failedCount,
        int cancelledCount,
        TimeSpan totalDuration,
        bool wasCancelled)
    {
        var summary = wasCancelled
            ? FormatLocalizedText(
                "mainWindow.message.batchSummaryCancelled",
                $"任务已取消，成功 {successCount} 个，失败 {failedCount} 个，取消 {cancelledCount} 个",
                ("successCount", successCount),
                ("failedCount", failedCount),
                ("cancelledCount", cancelledCount))
            : failedCount == 0
                ? FormatLocalizedText(
                    "mainWindow.message.batchSummarySuccess",
                    $"处理完成，成功 {successCount} 个文件",
                    ("successCount", successCount))
                : FormatLocalizedText(
                    "mainWindow.message.batchSummaryPartial",
                    $"处理完成，成功 {successCount} 个，失败 {failedCount} 个",
                    ("successCount", successCount),
                    ("failedCount", failedCount));

        return FormatLocalizedText(
            "mainWindow.message.batchSummaryWithDuration",
            $"{summary}，总用时 {FormatDuration(totalDuration)}。",
            ("summary", summary),
            ("duration", FormatDuration(totalDuration)));
    }

    private string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalMinutes >= 1)
        {
            var totalMinutes = (int)duration.TotalMinutes;
            return duration.Seconds == 0
                ? FormatLocalizedText(
                    "mainWindow.message.duration.minutesOnly",
                    $"{totalMinutes} 分钟",
                    ("minutes", totalMinutes))
                : FormatLocalizedText(
                    "mainWindow.message.duration.minutesSeconds",
                    $"{totalMinutes} 分 {duration.Seconds} 秒",
                    ("minutes", totalMinutes),
                    ("seconds", duration.Seconds));
        }

        var secondsText = Math.Max(duration.TotalSeconds, 0.1).ToString("F1");
        return FormatLocalizedText(
            "mainWindow.message.duration.seconds",
            $"{secondsText} 秒",
            ("seconds", secondsText));
    }

    private string ExtractFriendlyExceptionMessage(Exception exception)
    {
        return string.IsNullOrWhiteSpace(exception.Message)
            ? GetLocalizedText("mainWindow.message.fallbackReason", "请检查网络连接或运行时目录。")
            : exception.Message;
    }

    private static string? TryExtractMeaningfulErrorLine(string standardError)
    {
        if (string.IsNullOrWhiteSpace(standardError))
        {
            return null;
        }

        var lines = standardError
            .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        for (var index = lines.Length - 1; index >= 0; index--)
        {
            var line = lines[index];
            if (string.IsNullOrWhiteSpace(line) || IsIgnorableErrorLine(line))
            {
                continue;
            }

            if (line.StartsWith("[", StringComparison.OrdinalIgnoreCase))
            {
                var closingBracketIndex = line.LastIndexOf(']');
                if (closingBracketIndex >= 0 && closingBracketIndex < line.Length - 1)
                {
                    line = line[(closingBracketIndex + 1)..].Trim();
                }
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            return line.Length > 120
                ? $"{line[..117]}..."
                : line.TrimEnd('.');
        }

        return null;
    }

    private static bool IsIgnorableErrorLine(string line)
    {
        return line.StartsWith("frame=", StringComparison.OrdinalIgnoreCase) ||
               line.StartsWith("size=", StringComparison.OrdinalIgnoreCase) ||
               line.StartsWith("time=", StringComparison.OrdinalIgnoreCase) ||
               line.StartsWith("video:", StringComparison.OrdinalIgnoreCase) ||
               line.StartsWith("audio:", StringComparison.OrdinalIgnoreCase) ||
               line.StartsWith("subtitle:", StringComparison.OrdinalIgnoreCase) ||
               line.StartsWith("Input #", StringComparison.OrdinalIgnoreCase) ||
               line.StartsWith("Output #", StringComparison.OrdinalIgnoreCase) ||
               line.StartsWith("Stream mapping:", StringComparison.OrdinalIgnoreCase) ||
               line.StartsWith("Metadata:", StringComparison.OrdinalIgnoreCase) ||
               line.StartsWith("Duration:", StringComparison.OrdinalIgnoreCase) ||
               line.StartsWith("Press [q]", StringComparison.OrdinalIgnoreCase) ||
               line.StartsWith("configuration:", StringComparison.OrdinalIgnoreCase) ||
               line.StartsWith("libav", StringComparison.OrdinalIgnoreCase);
    }
}
