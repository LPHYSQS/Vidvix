using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Vidvix.Core.Interfaces;
using Vidvix.Core.Models;
using Vidvix.Utils;

namespace Vidvix.Services;

public sealed class AudioSeparationWorkflowService : IAudioSeparationWorkflowService
{
    private static readonly Regex DemucsProgressRegex = new(@"(?<percent>\d{1,3})%", RegexOptions.Compiled);
    private static readonly IReadOnlyList<AudioStemKind> OrderedStemKinds =
        new[]
        {
            AudioStemKind.Vocals,
            AudioStemKind.Drums,
            AudioStemKind.Bass,
            AudioStemKind.Other
        };

    private readonly ApplicationConfiguration _configuration;
    private readonly IFFmpegRuntimeService _ffmpegRuntimeService;
    private readonly IFFmpegService _ffmpegService;
    private readonly IMediaInfoService _mediaInfoService;
    private readonly IMediaProcessingCommandFactory _mediaProcessingCommandFactory;
    private readonly IFFmpegCommandBuilder _commandBuilder;
    private readonly IDemucsExecutionPlanner _demucsExecutionPlanner;
    private readonly ILogger _logger;

    public AudioSeparationWorkflowService(
        ApplicationConfiguration configuration,
        IFFmpegRuntimeService ffmpegRuntimeService,
        IFFmpegService ffmpegService,
        IMediaInfoService mediaInfoService,
        IMediaProcessingCommandFactory mediaProcessingCommandFactory,
        IFFmpegCommandBuilder commandBuilder,
        IDemucsExecutionPlanner demucsExecutionPlanner,
        ILogger logger)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _ffmpegRuntimeService = ffmpegRuntimeService ?? throw new ArgumentNullException(nameof(ffmpegRuntimeService));
        _ffmpegService = ffmpegService ?? throw new ArgumentNullException(nameof(ffmpegService));
        _mediaInfoService = mediaInfoService ?? throw new ArgumentNullException(nameof(mediaInfoService));
        _mediaProcessingCommandFactory = mediaProcessingCommandFactory ?? throw new ArgumentNullException(nameof(mediaProcessingCommandFactory));
        _commandBuilder = commandBuilder ?? throw new ArgumentNullException(nameof(commandBuilder));
        _demucsExecutionPlanner = demucsExecutionPlanner ?? throw new ArgumentNullException(nameof(demucsExecutionPlanner));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<AudioSeparationResult> SeparateAsync(
        AudioSeparationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!File.Exists(request.InputPath))
        {
            throw new FileNotFoundException("未找到待拆音的输入文件。", request.InputPath);
        }

        var startedAt = DateTimeOffset.UtcNow;
        var ffmpegResolution = await _ffmpegRuntimeService.EnsureAvailableAsync(cancellationToken).ConfigureAwait(false);
        await ValidateInputAsync(request.InputPath, cancellationToken).ConfigureAwait(false);

        var normalizedOutputDirectory = ResolveOutputDirectory(request.InputPath, request.OutputDirectory);
        Directory.CreateDirectory(normalizedOutputDirectory);

        var temporaryRootPath = Path.Combine(
            Path.GetTempPath(),
            _configuration.LocalDataDirectoryName,
            "SplitAudio",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(temporaryRootPath);

        try
        {
            var normalizedInputPath = Path.Combine(temporaryRootPath, "input.wav");
            await NormalizeInputAsync(
                ffmpegResolution.ExecutablePath,
                request.InputPath,
                normalizedInputPath,
                request.Progress,
                cancellationToken).ConfigureAwait(false);

            ReportProgress(
                request.Progress,
                "运行策略",
                request.AccelerationMode == DemucsAccelerationMode.GpuPreferred
                    ? "已选择 GPU 优先模式，正在检测独显与核显..."
                    : "已选择 CPU 模式，正在准备 Demucs 运行时...",
                0.15d);

            var executionPlans = await _demucsExecutionPlanner
                .ResolveExecutionPlansAsync(request.AccelerationMode, cancellationToken)
                .ConfigureAwait(false);
            var executionPlan = executionPlans[0];

            ReportProgress(
                request.Progress,
                "运行策略",
                executionPlan.ResolutionSummary,
                0.15d);

            var demucsOutputRootPath = Path.Combine(temporaryRootPath, "demucs-output");
            executionPlan = await RunDemucsWithFallbackAsync(
                executionPlans,
                normalizedInputPath,
                demucsOutputRootPath,
                request.Progress,
                cancellationToken).ConfigureAwait(false);

            var stemSourcePaths = ResolveStemSourcePaths(demucsOutputRootPath);
            var usedOutputPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var stemOutputs = new List<AudioSeparationStemOutput>(OrderedStemKinds.Count);

            for (var index = 0; index < OrderedStemKinds.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var stemKind = OrderedStemKinds[index];
                var sourceStemPath = stemSourcePaths[stemKind];
                var targetStemPath = MediaPathResolver.CreateUniqueOutputPath(
                    MediaPathResolver.CreateOutputPath(
                        request.InputPath,
                        request.OutputFormat.Extension,
                        normalizedOutputDirectory,
                        $"_{GetStemFileSuffix(stemKind)}"),
                    usedOutputPaths);

                await ExportStemAsync(
                    ffmpegResolution.ExecutablePath,
                    sourceStemPath,
                    targetStemPath,
                    request.OutputFormat,
                    index,
                    request.Progress,
                    cancellationToken).ConfigureAwait(false);

                stemOutputs.Add(new AudioSeparationStemOutput(stemKind, targetStemPath));
            }

            ReportProgress(
                request.Progress,
                "拆音完成",
                $"已生成 {stemOutputs.Count} 条分轨文件。",
                1d);

            return new AudioSeparationResult(
                request.InputPath,
                normalizedOutputDirectory,
                stemOutputs,
                DateTimeOffset.UtcNow - startedAt,
                executionPlan);
        }
        finally
        {
            TryDeleteDirectory(temporaryRootPath);
        }
    }

    private async Task ValidateInputAsync(string inputPath, CancellationToken cancellationToken)
    {
        if (_mediaInfoService.TryGetCachedDetails(inputPath, out var cachedSnapshot))
        {
            if (!cachedSnapshot.HasAudioStream)
            {
                throw new InvalidOperationException("当前文件不包含可用音频轨道，无法执行拆音。");
            }

            return;
        }

        var detailsResult = await _mediaInfoService.GetMediaDetailsAsync(inputPath, cancellationToken).ConfigureAwait(false);
        if (!detailsResult.IsSuccess)
        {
            _logger.Log(LogLevel.Warning, $"拆音前未能完整读取媒体轨道信息，将继续尝试处理：{inputPath}");
            return;
        }

        if (detailsResult.Snapshot is { HasAudioStream: false })
        {
            throw new InvalidOperationException("当前文件不包含可用音频轨道，无法执行拆音。");
        }
    }

    private async Task NormalizeInputAsync(
        string ffmpegExecutablePath,
        string inputPath,
        string normalizedInputPath,
        IProgress<AudioSeparationProgress>? progress,
        CancellationToken cancellationToken)
    {
        ReportProgress(progress, "准备输入音频", "正在提取并标准化音频轨道...", 0d);

        var duration = await TryGetMediaDurationAsync(inputPath, cancellationToken).ConfigureAwait(false);
        var ffmpegProgress = new Progress<FFmpegProgressUpdate>(update =>
        {
            var ratio = update.ProgressRatio is double progressRatio
                ? 0.15d * Math.Clamp(progressRatio, 0d, 1d)
                : (double?)null;

            ReportProgress(
                progress,
                "准备输入音频",
                update.ProcessedDuration is { } processed && (update.TotalDuration ?? duration) is { } total
                    ? $"正在标准化音频：{FormatClockDuration(processed)} / {FormatClockDuration(total)}"
                    : "正在提取并标准化音频轨道...",
                ratio);
        });

        var command = _commandBuilder
            .Reset()
            .SetExecutablePath(ffmpegExecutablePath)
            .AddGlobalParameter("-hide_banner")
            .AddGlobalParameter("-y")
            .SetInput(inputPath)
            .SetOutput(normalizedInputPath)
            .AddParameter("-map", "0:a:0")
            .AddParameter("-vn")
            .AddParameter("-sn")
            .AddParameter("-dn")
            .AddParameter("-ac", "2")
            .AddParameter("-ar", "44100")
            .AddParameter("-c:a", "pcm_s16le")
            .Build();

        var result = await _ffmpegService.ExecuteAsync(
            command,
            new FFmpegExecutionOptions
            {
                InputDuration = duration,
                Progress = ffmpegProgress
            },
            cancellationToken).ConfigureAwait(false);

        EnsureFfmpegSucceeded(result, "标准化输入音频失败。");

        ReportProgress(progress, "准备输入音频", "输入音频已标准化完成。", 0.15d);
    }

    private async Task RunDemucsAsync(
        DemucsExecutionPlan executionPlan,
        string normalizedInputPath,
        string demucsOutputRootPath,
        IProgress<AudioSeparationProgress>? progress,
        CancellationToken cancellationToken)
    {
        ReportProgress(progress, "Demucs 分离", "正在运行 Demucs 拆分四轨...", 0.15d);

        using var process = new Process
        {
            StartInfo = CreateDemucsStartInfo(
                executionPlan,
                normalizedInputPath,
                demucsOutputRootPath),
            EnableRaisingEvents = true
        };

        var standardOutputBuilder = new StringBuilder();
        var standardErrorBuilder = new StringBuilder();
        var standardOutputClosed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var standardErrorClosed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        process.OutputDataReceived += (_, eventArgs) =>
        {
            if (eventArgs.Data is null)
            {
                standardOutputClosed.TrySetResult();
                return;
            }

            standardOutputBuilder.AppendLine(eventArgs.Data);
            TryReportDemucsProgress(progress, eventArgs.Data);
        };

        process.ErrorDataReceived += (_, eventArgs) =>
        {
            if (eventArgs.Data is null)
            {
                standardErrorClosed.TrySetResult();
                return;
            }

            standardErrorBuilder.AppendLine(eventArgs.Data);
            TryReportDemucsProgress(progress, eventArgs.Data);
        };

        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException("Demucs 进程未能成功启动。");
            }
        }
        catch (Win32Exception exception)
        {
            throw new InvalidOperationException("Demucs 运行时不可用，请检查离线运行时包是否完整。", exception);
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var cancellationRegistration = cancellationToken.Register(() => TryTerminateProcess(process));

        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await Task.WhenAll(standardOutputClosed.Task, standardErrorClosed.Task).ConfigureAwait(false);
            throw;
        }

        await Task.WhenAll(standardOutputClosed.Task, standardErrorClosed.Task).ConfigureAwait(false);

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                CreateDemucsFailureMessage(
                    process.ExitCode,
                    standardOutputBuilder.ToString(),
                    standardErrorBuilder.ToString()));
        }

        ReportProgress(progress, "Demucs 分离", "四轨 WAV 已生成，正在准备导出。", 0.80d);
    }

    private async Task<DemucsExecutionPlan> RunDemucsWithFallbackAsync(
        IReadOnlyList<DemucsExecutionPlan> executionPlans,
        string normalizedInputPath,
        string demucsOutputRootPath,
        IProgress<AudioSeparationProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (executionPlans.Count == 0)
        {
            throw new InvalidOperationException("未找到可用的 Demucs 执行方案。");
        }

        ExceptionDispatchInfo? lastFailure = null;

        for (var attemptIndex = 0; attemptIndex < executionPlans.Count; attemptIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var executionPlan = executionPlans[attemptIndex];
            TryDeleteDirectory(demucsOutputRootPath);
            Directory.CreateDirectory(demucsOutputRootPath);

            ReportProgress(
                progress,
                "运行策略",
                executionPlan.ResolutionSummary,
                0.15d);

            try
            {
                await RunDemucsAsync(
                    executionPlan,
                    normalizedInputPath,
                    demucsOutputRootPath,
                    progress,
                    cancellationToken).ConfigureAwait(false);

                return executionPlan;
            }
            catch (Exception exception) when (exception is not OperationCanceledException && attemptIndex < executionPlans.Count - 1)
            {
                lastFailure = ExceptionDispatchInfo.Capture(exception);
                _logger.Log(
                    LogLevel.Warning,
                    $"Demucs 执行失败，准备回退到下一个方案：{executionPlan.DeviceDisplayName} ({executionPlan.DeviceArgument})",
                    exception);
            }
        }

        lastFailure?.Throw();
        throw new InvalidOperationException("Demucs 执行失败，且没有可用的回退方案。");
    }

    private ProcessStartInfo CreateDemucsStartInfo(
        DemucsExecutionPlan executionPlan,
        string normalizedInputPath,
        string demucsOutputRootPath)
    {
        var demucsRuntime = executionPlan.RuntimeResolution;
        var startInfo = new ProcessStartInfo
        {
            FileName = demucsRuntime.PythonExecutablePath,
            WorkingDirectory = demucsRuntime.RuntimeRootPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        startInfo.Environment["PYTHONUTF8"] = "1";
        startInfo.ArgumentList.Add(executionPlan.LauncherScriptPath);
        startInfo.ArgumentList.Add("separate");
        startInfo.ArgumentList.Add("--");
        startInfo.ArgumentList.Add("-n");
        startInfo.ArgumentList.Add(_configuration.DemucsModelName);
        startInfo.ArgumentList.Add("--repo");
        startInfo.ArgumentList.Add(demucsRuntime.ModelRepositoryPath);
        startInfo.ArgumentList.Add("-o");
        startInfo.ArgumentList.Add(demucsOutputRootPath);
        startInfo.ArgumentList.Add("-d");
        startInfo.ArgumentList.Add(executionPlan.DeviceArgument);
        startInfo.ArgumentList.Add(normalizedInputPath);
        return startInfo;
    }

    private void TryReportDemucsProgress(IProgress<AudioSeparationProgress>? progress, string line)
    {
        if (progress is null || string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        var trimmedLine = line.Trim();
        if (string.IsNullOrWhiteSpace(trimmedLine))
        {
            return;
        }

        var match = DemucsProgressRegex.Match(trimmedLine);
        if (match.Success &&
            int.TryParse(match.Groups["percent"].Value, out var percentValue))
        {
            var normalizedPercent = Math.Clamp(percentValue / 100d, 0d, 1d);
            ReportProgress(
                progress,
                "Demucs 分离",
                trimmedLine,
                0.15d + (0.65d * normalizedPercent));
            return;
        }

        ReportProgress(progress, "Demucs 分离", trimmedLine, null);
    }

    private IReadOnlyDictionary<AudioStemKind, string> ResolveStemSourcePaths(string demucsOutputRootPath)
    {
        var stemPaths = new Dictionary<AudioStemKind, string>();

        foreach (var stemKind in OrderedStemKinds)
        {
            var stemFileName = $"{GetStemFileSuffix(stemKind)}.wav";
            var sourceStemPath = Directory.EnumerateFiles(
                    demucsOutputRootPath,
                    stemFileName,
                    SearchOption.AllDirectories)
                .FirstOrDefault();

            if (string.IsNullOrWhiteSpace(sourceStemPath))
            {
                throw new InvalidOperationException($"Demucs 未输出 {stemFileName}。");
            }

            stemPaths[stemKind] = sourceStemPath;
        }

        return stemPaths;
    }

    private async Task ExportStemAsync(
        string ffmpegExecutablePath,
        string sourceStemPath,
        string targetStemPath,
        OutputFormatOption outputFormat,
        int stemIndex,
        IProgress<AudioSeparationProgress>? progress,
        CancellationToken cancellationToken)
    {
        var stemKind = OrderedStemKinds[stemIndex];
        var stageTitle = $"导出 {GetStemDisplayName(stemKind)}";

        ReportProgress(
            progress,
            stageTitle,
            $"正在导出 {GetStemDisplayName(stemKind)} 分轨...",
            0.80d + ((stemIndex / (double)OrderedStemKinds.Count) * 0.20d));

        var duration = await TryGetMediaDurationAsync(sourceStemPath, cancellationToken).ConfigureAwait(false);
        var ffmpegProgress = new Progress<FFmpegProgressUpdate>(update =>
        {
            double? ratio = null;
            if (update.ProgressRatio is double progressRatio)
            {
                var normalizedStemProgress = (stemIndex + Math.Clamp(progressRatio, 0d, 1d)) / OrderedStemKinds.Count;
                ratio = 0.80d + (normalizedStemProgress * 0.20d);
            }

            ReportProgress(
                progress,
                stageTitle,
                update.ProcessedDuration is { } processed && (update.TotalDuration ?? duration) is { } total
                    ? $"正在导出 {GetStemDisplayName(stemKind)}：{FormatClockDuration(processed)} / {FormatClockDuration(total)}"
                    : $"正在导出 {GetStemDisplayName(stemKind)} 分轨...",
                ratio);
        });

        var executionContext = new MediaProcessingContext(
            ProcessingWorkspaceKind.Audio,
            ProcessingMode.AudioTrackExtract,
            outputFormat,
            TranscodingMode.FullTranscode,
            IsGpuAccelerationRequested: false,
            VideoAccelerationKind.None);

        var command = _mediaProcessingCommandFactory.Create(
            new MediaProcessingCommandRequest(
                ffmpegExecutablePath,
                sourceStemPath,
                targetStemPath,
                executionContext));

        var result = await _ffmpegService.ExecuteAsync(
            command,
            new FFmpegExecutionOptions
            {
                InputDuration = duration,
                Progress = ffmpegProgress
            },
            cancellationToken).ConfigureAwait(false);

        EnsureFfmpegSucceeded(result, $"导出 {GetStemDisplayName(stemKind)} 分轨失败。");
    }

    private async Task<TimeSpan?> TryGetMediaDurationAsync(string inputPath, CancellationToken cancellationToken)
    {
        if (_mediaInfoService.TryGetCachedDetails(inputPath, out var cachedSnapshot))
        {
            return cachedSnapshot.MediaDuration;
        }

        var result = await _mediaInfoService.GetMediaDetailsAsync(inputPath, cancellationToken).ConfigureAwait(false);
        return result.Snapshot?.MediaDuration;
    }

    private string ResolveOutputDirectory(string inputPath, string? outputDirectory)
    {
        if (MediaPathResolver.TryNormalizeOutputDirectory(outputDirectory, out var normalizedOutputDirectory))
        {
            return string.IsNullOrWhiteSpace(normalizedOutputDirectory)
                ? Path.GetDirectoryName(inputPath)
                    ?? throw new InvalidOperationException("输入文件缺少有效目录。")
                : normalizedOutputDirectory;
        }

        throw new InvalidOperationException("输出目录无效，请重新选择。");
    }

    private void EnsureFfmpegSucceeded(FFmpegExecutionResult result, string failureTitle)
    {
        if (result.WasSuccessful)
        {
            return;
        }

        if (result.WasCancelled)
        {
            throw new OperationCanceledException("当前拆音任务已取消。");
        }

        throw new InvalidOperationException($"{failureTitle}{ExtractFriendlyFailureMessage(result)}");
    }

    private static string CreateDemucsFailureMessage(int exitCode, string standardOutput, string standardError)
    {
        var outputLines = (standardError + Environment.NewLine + standardOutput)
            .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var lastMeaningfulLine = outputLines.LastOrDefault();
        return string.IsNullOrWhiteSpace(lastMeaningfulLine)
            ? $"Demucs 执行失败，退出代码：{exitCode}。"
            : $"Demucs 执行失败，退出代码：{exitCode}。{lastMeaningfulLine}";
    }

    private static string ExtractFriendlyFailureMessage(FFmpegExecutionResult result)
    {
        if (!string.IsNullOrWhiteSpace(result.FailureReason))
        {
            return result.FailureReason;
        }

        var lines = (result.StandardError ?? string.Empty)
            .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return lines.LastOrDefault() ?? "FFmpeg 未返回可用错误信息。";
    }

    private static void TryTerminateProcess(Process process)
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
    }

    private static void ReportProgress(
        IProgress<AudioSeparationProgress>? progress,
        string stageTitle,
        string detailText,
        double? progressRatio)
    {
        progress?.Report(new AudioSeparationProgress(
            stageTitle,
            detailText,
            progressRatio is double ratio ? Math.Clamp(ratio, 0d, 1d) : null));
    }

    private static string GetStemFileSuffix(AudioStemKind stemKind) =>
        stemKind switch
        {
            AudioStemKind.Vocals => "vocals",
            AudioStemKind.Drums => "drums",
            AudioStemKind.Bass => "bass",
            AudioStemKind.Other => "other",
            _ => throw new InvalidEnumArgumentException(nameof(stemKind), (int)stemKind, typeof(AudioStemKind))
        };

    private static string GetStemDisplayName(AudioStemKind stemKind) =>
        stemKind switch
        {
            AudioStemKind.Vocals => "人声",
            AudioStemKind.Drums => "鼓组",
            AudioStemKind.Bass => "低频",
            AudioStemKind.Other => "其他",
            _ => throw new InvalidEnumArgumentException(nameof(stemKind), (int)stemKind, typeof(AudioStemKind))
        };

    private static string FormatClockDuration(TimeSpan duration) =>
        duration.TotalHours >= 1d
            ? duration.ToString(@"hh\:mm\:ss")
            : duration.ToString(@"mm\:ss");

    private static void TryDeleteDirectory(string directoryPath)
    {
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
}
