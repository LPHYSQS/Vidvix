using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Vidvix.Core.Models;
using Vidvix.Services.FFmpeg;

namespace Vidvix.Services;

public sealed partial class VideoTrimWorkflowService
{
    private static readonly TimeSpan MinimumSmartTrimSelectionDuration = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan MinimumSmartTrimMiddleDuration = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan SmartTrimKeyframeTolerance = TimeSpan.FromMilliseconds(60);

    private async Task<SmartTrimAttemptResult> TryExecuteSmartTrimAsync(
        VideoTrimExportRequest request,
        string ffmpegExecutablePath,
        MediaDetailsSnapshot inputSnapshot,
        IProgress<FFmpegProgressUpdate>? progress,
        Action? onCpuFallback,
        CancellationToken cancellationToken)
    {
        if (!TryValidateSmartTrimPrerequisites(request, inputSnapshot, out var prerequisiteFailure))
        {
            return new SmartTrimAttemptResult(null, prerequisiteFailure);
        }

        var keyframes = await LoadVideoKeyframesAsync(ffmpegExecutablePath, request.InputPath, cancellationToken).ConfigureAwait(false);
        var plan = TryBuildSmartTrimPlan(request, keyframes);
        if (plan is null)
        {
            return new SmartTrimAttemptResult(null, "当前片段未覆盖足够的关键帧区间，已回退为整段精确重编码。");
        }

        var executionResult = await ExecuteSmartTrimPipelineAsync(
                request,
                ffmpegExecutablePath,
                inputSnapshot.HasAudioStream,
                plan,
                request.VideoAccelerationKind,
                progress,
                cancellationToken)
            .ConfigureAwait(false);

        if (executionResult.WasSuccessful)
        {
            return new SmartTrimAttemptResult(
                CreateSmartTrimExportResult(request, executionResult, usedCpuFallback: false),
                null);
        }

        if (executionResult.WasCancelled || executionResult.TimedOut)
        {
            return new SmartTrimAttemptResult(
                CreateSmartTrimExportResult(request, executionResult, usedCpuFallback: false),
                null);
        }

        if (request.VideoAccelerationKind != VideoAccelerationKind.None)
        {
            onCpuFallback?.Invoke();

            var cpuRequest = request with
            {
                VideoAccelerationKind = VideoAccelerationKind.None
            };

            executionResult = await ExecuteSmartTrimPipelineAsync(
                    cpuRequest,
                    ffmpegExecutablePath,
                    inputSnapshot.HasAudioStream,
                    plan,
                    VideoAccelerationKind.None,
                    progress,
                    cancellationToken)
                .ConfigureAwait(false);

            if (executionResult.WasSuccessful)
            {
                return new SmartTrimAttemptResult(
                    CreateSmartTrimExportResult(cpuRequest, executionResult, usedCpuFallback: true),
                    null);
            }

            if (executionResult.WasCancelled || executionResult.TimedOut)
            {
                return new SmartTrimAttemptResult(
                    CreateSmartTrimExportResult(cpuRequest, executionResult, usedCpuFallback: true),
                    null);
            }
        }

        return new SmartTrimAttemptResult(null, "smart trim 未能稳定完成，已回退为整段精确重编码。");
    }

    private static bool TryValidateSmartTrimPrerequisites(
        VideoTrimExportRequest request,
        MediaDetailsSnapshot inputSnapshot,
        out string failureMessage)
    {
        if (request.Duration < MinimumSmartTrimSelectionDuration)
        {
            failureMessage = "当前片段较短，直接整段精确重编码更稳定，已自动回退。";
            return false;
        }

        if (!SupportsSmartTrimOutput(request.OutputFormat.Extension))
        {
            failureMessage = "当前输出格式不适合 smart trim，已回退为整段精确重编码。";
            return false;
        }

        if (!string.Equals(NormalizeVideoCodecName(inputSnapshot.PrimaryVideoCodecName), "h264", StringComparison.Ordinal))
        {
            failureMessage = "当前素材视频编码暂不适合 smart trim，已回退为整段精确重编码。";
            return false;
        }

        failureMessage = string.Empty;
        return true;
    }

    private async Task<IReadOnlyList<TimeSpan>> LoadVideoKeyframesAsync(
        string ffmpegExecutablePath,
        string inputPath,
        CancellationToken cancellationToken)
    {
        var ffprobePath = ResolveFfprobePath(ffmpegExecutablePath);
        if (string.IsNullOrWhiteSpace(ffprobePath) || !File.Exists(ffprobePath))
        {
            return Array.Empty<TimeSpan>();
        }

        using var process = new Process
        {
            StartInfo = CreateKeyframeProbeStartInfo(ffprobePath, inputPath),
            EnableRaisingEvents = true
        };

        if (!process.Start())
        {
            return Array.Empty<TimeSpan>();
        }

        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();

        using var registration = cancellationToken.Register(() =>
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
            }
        });

        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        await Task.WhenAll(outputTask, errorTask).ConfigureAwait(false);

        if (process.ExitCode != 0)
        {
            return Array.Empty<TimeSpan>();
        }

        var keyframes = outputTask.Result
            .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(ParseKeyframeTimestamp)
            .Where(static time => time is not null)
            .Select(static time => time!.Value)
            .Distinct()
            .OrderBy(static time => time)
            .ToList();

        if (keyframes.Count == 0 || keyframes[0] > TimeSpan.Zero)
        {
            keyframes.Insert(0, TimeSpan.Zero);
        }

        return keyframes;
    }

    private static SmartTrimPlan? TryBuildSmartTrimPlan(
        VideoTrimExportRequest request,
        IReadOnlyList<TimeSpan> keyframes)
    {
        if (keyframes.Count == 0)
        {
            return null;
        }

        var alignedStart = ResolveAlignedMiddleStart(request.StartPosition, keyframes);
        var alignedEnd = ResolveAlignedMiddleEnd(request.EndPosition, keyframes);
        if (alignedStart is null || alignedEnd is null || alignedEnd <= alignedStart)
        {
            return null;
        }

        var plan = new SmartTrimPlan(
            request.StartPosition,
            alignedStart.Value,
            alignedEnd.Value,
            request.EndPosition);

        return plan.MiddleDuration >= MinimumSmartTrimMiddleDuration ? plan : null;
    }

    private async Task<FFmpegExecutionResult> ExecuteSmartTrimPipelineAsync(
        VideoTrimExportRequest request,
        string ffmpegExecutablePath,
        bool hasAudioStream,
        SmartTrimPlan plan,
        VideoAccelerationKind accelerationKind,
        IProgress<FFmpegProgressUpdate>? progress,
        CancellationToken cancellationToken)
    {
        var temporaryDirectoryPath = Path.Combine(Path.GetTempPath(), $"vidvix-smart-trim-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporaryDirectoryPath);

        try
        {
            var headVideoPath = Path.Combine(temporaryDirectoryPath, "head.ts");
            var middleVideoPath = Path.Combine(temporaryDirectoryPath, "middle.ts");
            var tailVideoPath = Path.Combine(temporaryDirectoryPath, "tail.ts");
            var combinedVideoPath = Path.Combine(temporaryDirectoryPath, "combined.ts");
            var temporaryAudioPath = Path.Combine(temporaryDirectoryPath, "audio.m4a");
            var videoConcatListPath = Path.Combine(temporaryDirectoryPath, "video.concat.txt");

            var plannedVideoSegments = new List<string>(capacity: 3);
            var stageCount = CountSmartTrimStages(plan, hasAudioStream);
            var stageIndex = 0;

            if (plan.HasHead)
            {
                var headResult = await _ffmpegService.ExecuteAsync(
                        CreateSmartTrimVideoEncodeCommand(
                            ffmpegExecutablePath,
                            request.InputPath,
                            plan.SelectionStart,
                            plan.HeadDuration,
                            headVideoPath,
                            accelerationKind),
                        CreateStageOptions(plan.HeadDuration, request.Duration, progress, stageIndex++, stageCount),
                        cancellationToken)
                    .ConfigureAwait(false);
                if (!headResult.WasSuccessful)
                {
                    return headResult;
                }

                plannedVideoSegments.Add(headVideoPath);
            }

            var middleResult = await _ffmpegService.ExecuteAsync(
                    CreateSmartTrimVideoCopyCommand(
                        ffmpegExecutablePath,
                        request.InputPath,
                        plan.MiddleStart,
                        plan.MiddleDuration,
                        middleVideoPath),
                    CreateStageOptions(plan.MiddleDuration, request.Duration, progress, stageIndex++, stageCount),
                    cancellationToken)
                .ConfigureAwait(false);
            if (!middleResult.WasSuccessful)
            {
                return middleResult;
            }

            plannedVideoSegments.Add(middleVideoPath);

            if (plan.HasTail)
            {
                var tailResult = await _ffmpegService.ExecuteAsync(
                        CreateSmartTrimVideoEncodeCommand(
                            ffmpegExecutablePath,
                            request.InputPath,
                            plan.TailStart,
                            plan.TailDuration,
                            tailVideoPath,
                            accelerationKind),
                        CreateStageOptions(plan.TailDuration, request.Duration, progress, stageIndex++, stageCount),
                        cancellationToken)
                    .ConfigureAwait(false);
                if (!tailResult.WasSuccessful)
                {
                    return tailResult;
                }

                plannedVideoSegments.Add(tailVideoPath);
            }

            string preparedVideoPath;
            if (plannedVideoSegments.Count == 1)
            {
                preparedVideoPath = plannedVideoSegments[0];
            }
            else
            {
                File.WriteAllLines(
                    videoConcatListPath,
                    plannedVideoSegments.Select(static path => $"file '{EscapeConcatPath(path)}'"),
                    Encoding.UTF8);

                var concatResult = await _ffmpegService.ExecuteAsync(
                        CreateSmartTrimConcatCommand(
                            ffmpegExecutablePath,
                            videoConcatListPath,
                            combinedVideoPath),
                        CreateStageOptions(plan.SelectedDuration, request.Duration, progress, stageIndex++, stageCount),
                        cancellationToken)
                    .ConfigureAwait(false);
                if (!concatResult.WasSuccessful)
                {
                    return concatResult;
                }

                preparedVideoPath = combinedVideoPath;
            }

            string? preparedAudioPath = null;
            if (hasAudioStream)
            {
                var audioResult = await _ffmpegService.ExecuteAsync(
                        CreateSmartTrimAudioCommand(
                            ffmpegExecutablePath,
                            request.InputPath,
                            plan.SelectionStart,
                            plan.SelectedDuration,
                            temporaryAudioPath),
                        CreateStageOptions(plan.SelectedDuration, request.Duration, progress, stageIndex++, stageCount),
                        cancellationToken)
                    .ConfigureAwait(false);
                if (!audioResult.WasSuccessful)
                {
                    return audioResult;
                }

                preparedAudioPath = temporaryAudioPath;
            }

            return await _ffmpegService.ExecuteAsync(
                    CreateSmartTrimMuxCommand(
                        ffmpegExecutablePath,
                        preparedVideoPath,
                        preparedAudioPath,
                        request.OutputPath,
                        request.OutputFormat.Extension),
                    CreateStageOptions(plan.SelectedDuration, request.Duration, progress, stageIndex, stageCount),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            TryDeleteDirectory(temporaryDirectoryPath);
        }
    }

    private FFmpegExecutionOptions CreateStageOptions(
        TimeSpan stageDuration,
        TimeSpan totalDuration,
        IProgress<FFmpegProgressUpdate>? progress,
        int stageIndex,
        int stageCount) =>
        new()
        {
            Timeout = _configuration.DefaultExecutionTimeout,
            InputDuration = stageDuration > TimeSpan.Zero ? stageDuration : totalDuration,
            Progress = CreateStageProgress(progress, totalDuration, stageIndex, stageCount)
        };

    private VideoTrimExportResult CreateSmartTrimExportResult(
        VideoTrimExportRequest request,
        FFmpegExecutionResult executionResult,
        bool usedCpuFallback)
    {
        var message = usedCpuFallback
            ? "精确度优先已启用 smart trim，GPU 不可用时已自动回退到 CPU 完成导出。"
            : "精确度优先已启用 smart trim：头尾精确重编码，中段关键帧区间直拷贝。";

        return new VideoTrimExportResult(request, executionResult, message, usedFastPath: false, usedCpuFallback);
    }

    private ProcessStartInfo CreateKeyframeProbeStartInfo(string ffprobePath, string inputPath)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = ffprobePath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        startInfo.ArgumentList.Add("-v");
        startInfo.ArgumentList.Add("error");
        startInfo.ArgumentList.Add("-select_streams");
        startInfo.ArgumentList.Add("v:0");
        startInfo.ArgumentList.Add("-skip_frame");
        startInfo.ArgumentList.Add("nokey");
        startInfo.ArgumentList.Add("-show_frames");
        startInfo.ArgumentList.Add("-show_entries");
        startInfo.ArgumentList.Add("frame=best_effort_timestamp_time");
        startInfo.ArgumentList.Add("-of");
        startInfo.ArgumentList.Add("default=noprint_wrappers=1:nokey=1");
        startInfo.ArgumentList.Add(inputPath);
        return startInfo;
    }

    private FFmpegCommand CreateSmartTrimVideoEncodeCommand(
        string ffmpegExecutablePath,
        string inputPath,
        TimeSpan segmentStart,
        TimeSpan segmentDuration,
        string outputPath,
        VideoAccelerationKind accelerationKind)
    {
        var arguments = new List<string>
        {
            "-hide_banner",
            GetOverwriteArgument(),
            "-i",
            inputPath,
            "-ss",
            FormatTimestamp(segmentStart),
            "-t",
            FormatTimestamp(segmentDuration),
            "-map",
            "0:v:0",
            "-an",
            "-sn",
            "-dn"
        };

        FFmpegVideoEncodingPolicy.AppendH264Encoding(arguments, accelerationKind);
        arguments.Add("-f");
        arguments.Add("mpegts");
        arguments.Add(outputPath);
        return new FFmpegCommand(ffmpegExecutablePath, arguments);
    }

    private FFmpegCommand CreateSmartTrimVideoCopyCommand(
        string ffmpegExecutablePath,
        string inputPath,
        TimeSpan segmentStart,
        TimeSpan segmentDuration,
        string outputPath)
    {
        var arguments = new List<string>
        {
            "-hide_banner",
            GetOverwriteArgument(),
            "-ss",
            FormatTimestamp(segmentStart),
            "-i",
            inputPath,
            "-t",
            FormatTimestamp(segmentDuration),
            "-map",
            "0:v:0",
            "-an",
            "-sn",
            "-dn",
            "-c:v",
            "copy",
            "-f",
            "mpegts",
            outputPath
        };

        return new FFmpegCommand(ffmpegExecutablePath, arguments);
    }

    private FFmpegCommand CreateSmartTrimConcatCommand(
        string ffmpegExecutablePath,
        string concatListPath,
        string outputPath)
    {
        var arguments = new List<string>
        {
            "-hide_banner",
            GetOverwriteArgument(),
            "-f",
            "concat",
            "-safe",
            "0",
            "-i",
            concatListPath,
            "-c",
            "copy",
            outputPath
        };

        return new FFmpegCommand(ffmpegExecutablePath, arguments);
    }

    private FFmpegCommand CreateSmartTrimAudioCommand(
        string ffmpegExecutablePath,
        string inputPath,
        TimeSpan segmentStart,
        TimeSpan segmentDuration,
        string outputPath)
    {
        var arguments = new List<string>
        {
            "-hide_banner",
            GetOverwriteArgument(),
            "-i",
            inputPath,
            "-ss",
            FormatTimestamp(segmentStart),
            "-t",
            FormatTimestamp(segmentDuration),
            "-map",
            "0:a:0",
            "-vn",
            "-sn",
            "-dn",
            "-c:a",
            "aac",
            "-b:a",
            "256k",
            outputPath
        };

        return new FFmpegCommand(ffmpegExecutablePath, arguments);
    }

    private FFmpegCommand CreateSmartTrimMuxCommand(
        string ffmpegExecutablePath,
        string videoPath,
        string? audioPath,
        string outputPath,
        string outputExtension)
    {
        var normalizedExtension = outputExtension.Trim().ToLowerInvariant();
        var arguments = new List<string>
        {
            "-hide_banner",
            GetOverwriteArgument(),
            "-i",
            videoPath
        };

        if (!string.IsNullOrWhiteSpace(audioPath))
        {
            arguments.Add("-i");
            arguments.Add(audioPath);
        }

        arguments.Add("-map");
        arguments.Add("0:v:0");

        if (!string.IsNullOrWhiteSpace(audioPath))
        {
            arguments.Add("-map");
            arguments.Add("1:a:0");
        }

        arguments.Add("-c:v");
        arguments.Add("copy");

        if (!string.IsNullOrWhiteSpace(audioPath))
        {
            arguments.Add("-c:a");
            arguments.Add("copy");
            arguments.Add("-shortest");
        }
        else
        {
            arguments.Add("-an");
        }

        arguments.Add("-sn");
        arguments.Add("-dn");

        switch (normalizedExtension)
        {
            case ".mp4":
            case ".mov":
                arguments.Add("-movflags");
                arguments.Add("+faststart");
                break;
            case ".m4v":
                arguments.Add("-f");
                arguments.Add("mp4");
                arguments.Add("-movflags");
                arguments.Add("+faststart");
                break;
            case ".ts":
                arguments.Add("-f");
                arguments.Add("mpegts");
                break;
            case ".m2ts":
                arguments.Add("-f");
                arguments.Add("mpegts");
                arguments.Add("-mpegts_m2ts_mode");
                arguments.Add("1");
                break;
        }

        arguments.Add(outputPath);
        return new FFmpegCommand(ffmpegExecutablePath, arguments);
    }

    private string GetOverwriteArgument() => _configuration.OverwriteOutputFiles ? "-y" : "-n";

    private string ResolveFfprobePath(string ffmpegExecutablePath)
    {
        var executableDirectory = Path.GetDirectoryName(ffmpegExecutablePath);
        return string.IsNullOrWhiteSpace(executableDirectory)
            ? string.Empty
            : Path.Combine(executableDirectory, _configuration.FFprobeExecutableFileName);
    }

    private static IProgress<FFmpegProgressUpdate>? CreateStageProgress(
        IProgress<FFmpegProgressUpdate>? overallProgress,
        TimeSpan totalDuration,
        int stageIndex,
        int stageCount)
    {
        if (overallProgress is null || stageCount <= 0)
        {
            return null;
        }

        return new Progress<FFmpegProgressUpdate>(update =>
        {
            var stageRatio = update.ProgressRatio ?? (update.IsCompleted ? 1d : 0d);
            var overallRatio = Math.Clamp((stageIndex + stageRatio) / stageCount, 0d, 1d);
            TimeSpan? processedDuration = totalDuration > TimeSpan.Zero
                ? TimeSpan.FromTicks((long)Math.Round(totalDuration.Ticks * overallRatio, MidpointRounding.AwayFromZero))
                : null;

            overallProgress.Report(new FFmpegProgressUpdate(
                processedDuration,
                totalDuration > TimeSpan.Zero ? totalDuration : (TimeSpan?)null,
                overallRatio,
                update.IsCompleted && stageIndex >= stageCount - 1));
        });
    }

    private static int CountSmartTrimStages(SmartTrimPlan plan, bool hasAudioStream)
    {
        var count = 1; // final mux
        if (plan.HasHead)
        {
            count++;
        }

        count++; // middle copy

        if (plan.HasTail)
        {
            count++;
        }

        if (plan.HasHead || plan.HasTail)
        {
            count++;
        }

        if (hasAudioStream)
        {
            count++;
        }

        return count;
    }

    private static bool SupportsSmartTrimOutput(string extension) =>
        extension.Trim().ToLowerInvariant() is ".mp4" or ".mkv" or ".mov" or ".m4v" or ".ts" or ".m2ts";

    private static string NormalizeVideoCodecName(string? codecName) =>
        (codecName ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "avc" => "h264",
            "avc1" => "h264",
            "x264" => "h264",
            _ => (codecName ?? string.Empty).Trim().ToLowerInvariant()
        };

    private static TimeSpan? ParseKeyframeTimestamp(string line)
    {
        var candidate = line.Trim();
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return null;
        }

        return double.TryParse(candidate, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) &&
               seconds >= 0d
            ? TimeSpan.FromSeconds(seconds)
            : null;
    }

    private static TimeSpan? ResolveAlignedMiddleStart(TimeSpan selectionStart, IReadOnlyList<TimeSpan> keyframes)
    {
        foreach (var keyframe in keyframes)
        {
            if (Math.Abs((keyframe - selectionStart).TotalMilliseconds) <= SmartTrimKeyframeTolerance.TotalMilliseconds)
            {
                return selectionStart;
            }

            if (keyframe > selectionStart)
            {
                return keyframe;
            }
        }

        return null;
    }

    private static TimeSpan? ResolveAlignedMiddleEnd(TimeSpan selectionEnd, IReadOnlyList<TimeSpan> keyframes)
    {
        for (var index = keyframes.Count - 1; index >= 0; index--)
        {
            var keyframe = keyframes[index];
            if (Math.Abs((selectionEnd - keyframe).TotalMilliseconds) <= SmartTrimKeyframeTolerance.TotalMilliseconds)
            {
                return selectionEnd;
            }

            if (keyframe < selectionEnd)
            {
                return keyframe;
            }
        }

        return null;
    }

    private static string FormatTimestamp(TimeSpan time)
    {
        var totalHours = (int)time.TotalHours;
        return FormattableString.Invariant($"{totalHours:00}:{time.Minutes:00}:{time.Seconds:00}.{time.Milliseconds:000}");
    }

    private static string EscapeConcatPath(string path) =>
        path.Replace("\\", "/", StringComparison.Ordinal)
            .Replace("'", "'\\''", StringComparison.Ordinal);

    private static void TryDeleteDirectory(string? directoryPath)
    {
        if (string.IsNullOrWhiteSpace(directoryPath))
        {
            return;
        }

        try
        {
            if (Directory.Exists(directoryPath))
            {
                Directory.Delete(directoryPath, recursive: true);
            }
        }
        catch
        {
        }
    }

    private sealed record SmartTrimAttemptResult(
        VideoTrimExportResult? ExportResult,
        string? FallbackMessage);

    private sealed record SmartTrimPlan(
        TimeSpan SelectionStart,
        TimeSpan MiddleStart,
        TimeSpan MiddleEnd,
        TimeSpan SelectionEnd)
    {
        public TimeSpan HeadDuration => MiddleStart - SelectionStart;

        public TimeSpan MiddleDuration => MiddleEnd - MiddleStart;

        public TimeSpan TailStart => MiddleEnd;

        public TimeSpan TailDuration => SelectionEnd - MiddleEnd;

        public TimeSpan SelectedDuration => SelectionEnd - SelectionStart;

        public bool HasHead => HeadDuration > TimeSpan.Zero;

        public bool HasTail => TailDuration > TimeSpan.Zero;
    }
}
