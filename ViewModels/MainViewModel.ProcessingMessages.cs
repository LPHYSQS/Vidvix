using System;
using Vidvix.Core.Models;

namespace Vidvix.ViewModels;

public sealed partial class MainViewModel
{
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
                    ? "已打开输出文件所在文件夹，并选中处理完成的文件。"
                    : "已打开最后一个成功输出文件所在文件夹，并选中该输出文件。",
                clearExisting: false);
        }
        catch (Exception exception)
        {
            _logger.Log(LogLevel.Warning, "打开输出文件所在位置失败。", exception);
            AddUiLog(LogLevel.Warning, $"处理已完成，但未能打开输出文件所在位置。原因：{ExtractFriendlyExceptionMessage(exception)}", clearExisting: false);
        }
    }

    private string CreateFriendlyFailureMessage(FFmpegExecutionResult result)
    {
        var standardError = result.StandardError;

        if (result.TimedOut)
        {
            return "处理超时。";
        }

        if (standardError.Contains("matches no streams", StringComparison.OrdinalIgnoreCase))
        {
            if (IsAudioWorkspace)
            {
                return "该文件没有可转换的音频流。";
            }

            return SelectedProcessingMode.Mode switch
            {
                ProcessingMode.VideoTrackExtract => "该文件没有可提取的视频轨道。",
                ProcessingMode.AudioTrackExtract => "该文件没有可提取的音频轨道。",
                _ => "该文件缺少可处理的媒体流。"
            };
        }

        if (standardError.Contains("not currently supported in container", StringComparison.OrdinalIgnoreCase) ||
            standardError.Contains("Could not write header", StringComparison.OrdinalIgnoreCase))
        {
            return "目标格式与当前媒体流不兼容，请尝试 MP4、MKV、MOV 或更换其他导出格式。";
        }

        if (standardError.Contains("Permission denied", StringComparison.OrdinalIgnoreCase))
        {
            return "输出文件正在被占用，或当前目录没有写入权限。";
        }

        if (standardError.Contains("Unknown encoder", StringComparison.OrdinalIgnoreCase))
        {
            return "当前环境缺少所需格式支持，无法输出该格式。";
        }

        if (standardError.Contains("Invalid argument", StringComparison.OrdinalIgnoreCase))
        {
            return "当前输出格式或参数无效，无法完成处理。";
        }

        if (standardError.Contains("No such file or directory", StringComparison.OrdinalIgnoreCase))
        {
            return "输入文件不存在，或输出目录不可用。";
        }

        var extractedReason = TryExtractMeaningfulErrorLine(standardError);
        if (!string.IsNullOrWhiteSpace(extractedReason))
        {
            return extractedReason;
        }

        return result.FailureReason ?? "处理失败。";
    }

    private int MarkRemainingItemsCancelled(int startIndex)
    {
        var cancelledCount = 0;

        for (var index = startIndex; index < ImportItems.Count; index++)
        {
            var item = ImportItems[index];
            if (!item.IsPending)
            {
                continue;
            }

            item.MarkCancelled();
            cancelledCount++;
        }

        return cancelledCount;
    }

    private static string CreateBatchSummaryMessage(
        int successCount,
        int failedCount,
        int cancelledCount,
        TimeSpan totalDuration,
        bool wasCancelled)
    {
        var summary = wasCancelled
            ? $"任务已取消，成功 {successCount} 个，失败 {failedCount} 个，取消 {cancelledCount} 个"
            : failedCount == 0
                ? $"处理完成，成功 {successCount} 个文件"
                : $"处理完成，成功 {successCount} 个，失败 {failedCount} 个";

        return $"{summary}，总用时 {FormatDuration(totalDuration)}。";
    }

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalMinutes >= 1)
        {
            var totalMinutes = (int)duration.TotalMinutes;
            return duration.Seconds == 0
                ? $"{totalMinutes} 分钟"
                : $"{totalMinutes} 分 {duration.Seconds} 秒";
        }

        return $"{Math.Max(duration.TotalSeconds, 0.1):F1} 秒";
    }

    private static string ExtractFriendlyExceptionMessage(Exception exception)
    {
        return string.IsNullOrWhiteSpace(exception.Message)
            ? "请检查网络连接或运行时目录。"
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
