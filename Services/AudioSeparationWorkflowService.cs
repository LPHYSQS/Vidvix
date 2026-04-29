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
    private readonly ILocalizationService _localizationService;
    private readonly ILogger _logger;

    public AudioSeparationWorkflowService(
        ApplicationConfiguration configuration,
        IFFmpegRuntimeService ffmpegRuntimeService,
        IFFmpegService ffmpegService,
        IMediaInfoService mediaInfoService,
        IMediaProcessingCommandFactory mediaProcessingCommandFactory,
        IFFmpegCommandBuilder commandBuilder,
        IDemucsExecutionPlanner demucsExecutionPlanner,
        ILocalizationService localizationService,
        ILogger logger)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _ffmpegRuntimeService = ffmpegRuntimeService ?? throw new ArgumentNullException(nameof(ffmpegRuntimeService));
        _ffmpegService = ffmpegService ?? throw new ArgumentNullException(nameof(ffmpegService));
        _mediaInfoService = mediaInfoService ?? throw new ArgumentNullException(nameof(mediaInfoService));
        _mediaProcessingCommandFactory = mediaProcessingCommandFactory ?? throw new ArgumentNullException(nameof(mediaProcessingCommandFactory));
        _commandBuilder = commandBuilder ?? throw new ArgumentNullException(nameof(commandBuilder));
        _demucsExecutionPlanner = demucsExecutionPlanner ?? throw new ArgumentNullException(nameof(demucsExecutionPlanner));
        _localizationService = localizationService ?? throw new ArgumentNullException(nameof(localizationService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<AudioSeparationResult> SeparateAsync(
        AudioSeparationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!File.Exists(request.InputPath))
        {
            throw CreateLocalizedFileNotFoundException(
                "splitAudio.error.inputFileMissing",
                "未找到待拆音的输入文件。",
                request.InputPath);
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
                GetLocalizedText("splitAudio.progress.stage.strategy", "运行策略"),
                request.AccelerationMode == DemucsAccelerationMode.GpuPreferred
                    ? GetLocalizedText(
                        "splitAudio.progress.detail.strategy.gpuPreferred",
                        "已选择 GPU 优先模式，正在检测独显与核显...")
                    : GetLocalizedText(
                        "splitAudio.progress.detail.strategy.cpu",
                        "已选择 CPU 模式，正在准备 Demucs 运行时..."),
                0.15d,
                () => GetLocalizedText("splitAudio.progress.stage.strategy", "运行策略"),
                request.AccelerationMode == DemucsAccelerationMode.GpuPreferred
                    ? () => GetLocalizedText(
                        "splitAudio.progress.detail.strategy.gpuPreferred",
                        "已选择 GPU 优先模式，正在检测独显与核显...")
                    : () => GetLocalizedText(
                        "splitAudio.progress.detail.strategy.cpu",
                        "已选择 CPU 模式，正在准备 Demucs 运行时..."));

            var executionPlans = await _demucsExecutionPlanner
                .ResolveExecutionPlansAsync(request.AccelerationMode, cancellationToken)
                .ConfigureAwait(false);
            var executionPlan = executionPlans[0];

            ReportProgress(
                request.Progress,
                GetLocalizedText("splitAudio.progress.stage.strategy", "运行策略"),
                executionPlan.ResolveResolutionSummary(),
                0.15d,
                () => GetLocalizedText("splitAudio.progress.stage.strategy", "运行策略"),
                executionPlan.ResolveResolutionSummary);

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
                GetLocalizedText("splitAudio.progress.stage.complete", "拆音完成"),
                FormatLocalizedText(
                    "splitAudio.progress.detail.completed",
                    "已生成 {count} 条分轨文件。",
                    ("count", stemOutputs.Count)),
                1d,
                () => GetLocalizedText("splitAudio.progress.stage.complete", "拆音完成"),
                () => FormatLocalizedText(
                    "splitAudio.progress.detail.completed",
                    "已生成 {count} 条分轨文件。",
                    ("count", stemOutputs.Count)));

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
                throw CreateLocalizedInvalidOperationException(
                    "splitAudio.error.noAudioTrack",
                    "当前文件不包含可用音频轨道，无法执行拆音。");
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
            throw CreateLocalizedInvalidOperationException(
                "splitAudio.error.noAudioTrack",
                "当前文件不包含可用音频轨道，无法执行拆音。");
        }
    }

    private async Task NormalizeInputAsync(
        string ffmpegExecutablePath,
        string inputPath,
        string normalizedInputPath,
        IProgress<AudioSeparationProgress>? progress,
        CancellationToken cancellationToken)
    {
        ReportProgress(
            progress,
            GetLocalizedText("splitAudio.progress.stage.normalize", "准备输入音频"),
            GetLocalizedText("splitAudio.progress.detail.normalize.running", "正在提取并标准化音频轨道..."),
            0d,
            () => GetLocalizedText("splitAudio.progress.stage.normalize", "准备输入音频"),
            () => GetLocalizedText("splitAudio.progress.detail.normalize.running", "正在提取并标准化音频轨道..."));

        var duration = await TryGetMediaDurationAsync(inputPath, cancellationToken).ConfigureAwait(false);
        var ffmpegProgress = new Progress<FFmpegProgressUpdate>(update =>
        {
            var ratio = update.ProgressRatio is double progressRatio
                ? 0.15d * Math.Clamp(progressRatio, 0d, 1d)
                : (double?)null;

            ReportProgress(
                progress,
                GetLocalizedText("splitAudio.progress.stage.normalize", "准备输入音频"),
                update.ProcessedDuration is { } processed && (update.TotalDuration ?? duration) is { } total
                    ? FormatLocalizedText(
                        "splitAudio.progress.detail.normalize.duration",
                        "正在标准化音频：{processed} / {total}",
                        ("processed", FormatClockDuration(processed)),
                        ("total", FormatClockDuration(total)))
                    : GetLocalizedText("splitAudio.progress.detail.normalize.running", "正在提取并标准化音频轨道..."),
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

        EnsureFfmpegSucceeded(
            result,
            () => GetLocalizedText("splitAudio.error.normalizeFailed", "标准化输入音频失败。"));

        ReportProgress(
            progress,
            GetLocalizedText("splitAudio.progress.stage.normalize", "准备输入音频"),
            GetLocalizedText("splitAudio.progress.detail.normalize.completed", "输入音频已标准化完成。"),
            0.15d,
            () => GetLocalizedText("splitAudio.progress.stage.normalize", "准备输入音频"),
            () => GetLocalizedText("splitAudio.progress.detail.normalize.completed", "输入音频已标准化完成。"));
    }

    private async Task RunDemucsAsync(
        DemucsExecutionPlan executionPlan,
        string normalizedInputPath,
        string demucsOutputRootPath,
        IProgress<AudioSeparationProgress>? progress,
        CancellationToken cancellationToken)
    {
        ReportProgress(
            progress,
            GetLocalizedText("splitAudio.progress.stage.demucs", "Demucs 分离"),
            GetLocalizedText("splitAudio.progress.detail.demucs.running", "正在运行 Demucs 拆分四轨..."),
            0.15d,
            () => GetLocalizedText("splitAudio.progress.stage.demucs", "Demucs 分离"),
            () => GetLocalizedText("splitAudio.progress.detail.demucs.running", "正在运行 Demucs 拆分四轨..."));

        using var process = new Process
        {
            StartInfo = CreateDemucsStartInfo(
                executionPlan,
                normalizedInputPath,
                demucsOutputRootPath),
            EnableRaisingEvents = true
        };
        var processId = 0;

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
                throw CreateLocalizedInvalidOperationException(
                    "splitAudio.error.demucsProcessStartFailed",
                    "Demucs 进程未能成功启动。");
            }
            processId = process.Id;
        }
        catch (Win32Exception exception)
        {
            throw CreateLocalizedInvalidOperationException(
                exception,
                "splitAudio.error.demucsRuntimeUnavailable",
                "Demucs 运行时不可用，请检查离线运行时包是否完整。");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var cancellationRegistration = ExternalProcessTermination.RegisterTermination(process, cancellationToken);

        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await ExternalProcessTermination.WaitForTerminationAsync(
                    process,
                    processId,
                    _logger,
                    "拆音任务取消后，Demucs 进程在宽限时间内仍未完全退出，已继续返回结果。")
                .ConfigureAwait(false);
            await Task.WhenAll(standardOutputClosed.Task, standardErrorClosed.Task).ConfigureAwait(false);
            throw;
        }

        await Task.WhenAll(standardOutputClosed.Task, standardErrorClosed.Task).ConfigureAwait(false);

        var exitCode = TryGetProcessExitCode(process, out var exitCodeException);
        if (exitCode != 0)
        {
            throw new LocalizedInvalidOperationException(
                () => CreateDemucsFailureMessage(
                    exitCode,
                    standardOutputBuilder.ToString(),
                    standardErrorBuilder.ToString(),
                    exitCodeException),
                exitCodeException);
        }

        ReportProgress(
            progress,
            GetLocalizedText("splitAudio.progress.stage.demucs", "Demucs 分离"),
            GetLocalizedText("splitAudio.progress.detail.demucs.completed", "四轨 WAV 已生成，正在准备导出。"),
            0.80d,
            () => GetLocalizedText("splitAudio.progress.stage.demucs", "Demucs 分离"),
            () => GetLocalizedText("splitAudio.progress.detail.demucs.completed", "四轨 WAV 已生成，正在准备导出。"));
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
            throw CreateLocalizedInvalidOperationException(
                "splitAudio.error.noExecutionPlan",
                "未找到可用的 Demucs 执行方案。");
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
                GetLocalizedText("splitAudio.progress.stage.strategy", "运行策略"),
                executionPlan.ResolveResolutionSummary(),
                0.15d,
                () => GetLocalizedText("splitAudio.progress.stage.strategy", "运行策略"),
                executionPlan.ResolveResolutionSummary);

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
        throw CreateLocalizedInvalidOperationException(
            "splitAudio.error.noFallbackExecutionPlan",
            "Demucs 执行失败，且没有可用的回退方案。");
    }

    private ProcessStartInfo CreateDemucsStartInfo(
        DemucsExecutionPlan executionPlan,
        string normalizedInputPath,
        string demucsOutputRootPath)
    {
        var demucsRuntime = executionPlan.RuntimeResolution;
        var demucsStorageRootPath = ResolveDemucsStorageRootPath(demucsRuntime.RuntimeRootPath);
        var pythonCacheRootPath = EnsureDirectoryExists(Path.Combine(demucsStorageRootPath, "PyCache"));
        var torchCacheRootPath = EnsureDirectoryExists(Path.Combine(demucsStorageRootPath, "TorchCache"));
        var temporaryRootPath = EnsureDirectoryExists(Path.Combine(demucsStorageRootPath, "Temp"));
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
        startInfo.Environment["PYTHONDONTWRITEBYTECODE"] = "1";
        startInfo.Environment["PYTHONPYCACHEPREFIX"] = pythonCacheRootPath;
        startInfo.Environment["TORCH_HOME"] = torchCacheRootPath;
        startInfo.Environment["TEMP"] = temporaryRootPath;
        startInfo.Environment["TMP"] = temporaryRootPath;
        startInfo.Environment["TMPDIR"] = temporaryRootPath;
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
        startInfo.ArgumentList.Add("-j");
        startInfo.ArgumentList.Add("0");
        startInfo.ArgumentList.Add(normalizedInputPath);
        return startInfo;
    }

    private static string ResolveDemucsStorageRootPath(string runtimeRootPath)
    {
        var parentDirectoryPath = Path.GetDirectoryName(runtimeRootPath);
        return string.IsNullOrWhiteSpace(parentDirectoryPath)
            ? runtimeRootPath
            : parentDirectoryPath;
    }

    private static string EnsureDirectoryExists(string directoryPath)
    {
        Directory.CreateDirectory(directoryPath);
        return directoryPath;
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
                GetLocalizedText("splitAudio.progress.stage.demucs", "Demucs 分离"),
                trimmedLine,
                0.15d + (0.65d * normalizedPercent),
                () => GetLocalizedText("splitAudio.progress.stage.demucs", "Demucs 分离"));
            return;
        }

        ReportProgress(
            progress,
            GetLocalizedText("splitAudio.progress.stage.demucs", "Demucs 分离"),
            trimmedLine,
            null,
            () => GetLocalizedText("splitAudio.progress.stage.demucs", "Demucs 分离"));
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
                throw CreateLocalizedInvalidOperationException(
                    "splitAudio.error.stemOutputMissing",
                    "Demucs 未输出 {stemFileName}。",
                    ("stemFileName", stemFileName));
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
        var stemDisplayName = GetStemDisplayName(stemKind);
        var stageTitle = FormatLocalizedText(
            "splitAudio.progress.stage.exportStem",
            "导出 {stem}",
            ("stem", stemDisplayName));

        ReportProgress(
            progress,
            stageTitle,
            FormatLocalizedText(
                "splitAudio.progress.detail.exportStem.running",
                "正在导出 {stem} 分轨...",
                ("stem", stemDisplayName)),
            0.80d + ((stemIndex / (double)OrderedStemKinds.Count) * 0.20d),
            () => FormatLocalizedText(
                "splitAudio.progress.stage.exportStem",
                "导出 {stem}",
                ("stem", GetStemDisplayName(stemKind))),
            () => FormatLocalizedText(
                "splitAudio.progress.detail.exportStem.running",
                "正在导出 {stem} 分轨...",
                ("stem", GetStemDisplayName(stemKind))));

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
                    ? FormatLocalizedText(
                        "splitAudio.progress.detail.exportStem.duration",
                        "正在导出 {stem}：{processed} / {total}",
                        ("stem", GetStemDisplayName(stemKind)),
                        ("processed", FormatClockDuration(processed)),
                        ("total", FormatClockDuration(total)))
                    : FormatLocalizedText(
                        "splitAudio.progress.detail.exportStem.running",
                        "正在导出 {stem} 分轨...",
                        ("stem", GetStemDisplayName(stemKind))),
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

        EnsureFfmpegSucceeded(
            result,
            () => FormatLocalizedText(
                "splitAudio.error.exportStemFailed",
                "导出 {stem} 分轨失败。",
                ("stem", stemDisplayName)));
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
                    ?? throw CreateLocalizedInvalidOperationException(
                        "splitAudio.error.inputDirectoryMissing",
                        "输入文件缺少有效目录。")
                : normalizedOutputDirectory;
        }

        throw CreateLocalizedInvalidOperationException(
            "splitAudio.error.invalidOutputDirectory",
            "输出目录无效，请重新选择。");
    }

    private void EnsureFfmpegSucceeded(FFmpegExecutionResult result, Func<string> failureTitleResolver)
    {
        if (result.WasSuccessful)
        {
            return;
        }

        if (result.WasCancelled)
        {
            throw new OperationCanceledException(
                GetLocalizedText("splitAudio.status.cancelled", "当前拆音任务已取消。"));
        }

        throw new LocalizedInvalidOperationException(
            () => $"{failureTitleResolver()}{ExtractFriendlyFailureMessage(result)}");
    }

    private string CreateDemucsFailureMessage(
        int? exitCode,
        string standardOutput,
        string standardError,
        Exception? exitCodeException = null)
    {
        var outputLines = (standardError + Environment.NewLine + standardOutput)
            .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var lastMeaningfulLine = outputLines.LastOrDefault();
        if (exitCode is int resolvedExitCode)
        {
            return string.IsNullOrWhiteSpace(lastMeaningfulLine)
                ? FormatLocalizedText(
                    "splitAudio.error.demucsFailure.exitCode",
                    "Demucs 执行失败，退出代码：{exitCode}。",
                    ("exitCode", resolvedExitCode))
                : FormatLocalizedText(
                    "splitAudio.error.demucsFailure.exitCodeWithDetail",
                    "Demucs 执行失败，退出代码：{exitCode}。{detail}",
                    ("exitCode", resolvedExitCode),
                    ("detail", lastMeaningfulLine));
        }

        var fallbackDetail = !string.IsNullOrWhiteSpace(lastMeaningfulLine)
            ? lastMeaningfulLine
            : exitCodeException?.Message ?? GetLocalizedText(
                "splitAudio.error.demucsFailure.noDetail",
                "Demucs 未返回可用的错误信息。");

        return FormatLocalizedText(
            "splitAudio.error.demucsFailure.exitCodeUnavailable",
            "Demucs 进程异常终止，未能读取退出代码。{detail}",
            ("detail", fallbackDetail));
    }

    private string ExtractFriendlyFailureMessage(FFmpegExecutionResult result)
    {
        if (!string.IsNullOrWhiteSpace(result.FailureReason))
        {
            return result.FailureReason;
        }

        var lines = (result.StandardError ?? string.Empty)
            .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return lines.LastOrDefault() ?? GetLocalizedText(
            "splitAudio.error.noFfmpegMessage",
            "FFmpeg 未返回可用错误信息。");
    }

    private void ReportProgress(
        IProgress<AudioSeparationProgress>? progress,
        string stageTitle,
        string detailText,
        double? progressRatio,
        Func<string>? stageTitleResolver = null,
        Func<string>? detailTextResolver = null)
    {
        progress?.Report(new AudioSeparationProgress(
            stageTitle,
            detailText,
            progressRatio is double ratio ? Math.Clamp(ratio, 0d, 1d) : null,
            stageTitleResolver,
            detailTextResolver));
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

    private string GetStemDisplayName(AudioStemKind stemKind) =>
        stemKind switch
        {
            AudioStemKind.Vocals => GetLocalizedText("splitAudio.stem.vocals", "人声"),
            AudioStemKind.Drums => GetLocalizedText("splitAudio.stem.drums", "鼓组"),
            AudioStemKind.Bass => GetLocalizedText("splitAudio.stem.bass", "低频"),
            AudioStemKind.Other => GetLocalizedText("splitAudio.stem.other", "其他"),
            _ => throw new InvalidEnumArgumentException(nameof(stemKind), (int)stemKind, typeof(AudioStemKind))
        };

    private string GetLocalizedText(string key, string fallback) =>
        _localizationService.GetString(key, fallback);

    private LocalizedFileNotFoundException CreateLocalizedFileNotFoundException(
        string key,
        string fallback,
        string? fileName,
        params (string Name, object? Value)[] arguments) =>
        new(() => FormatLocalizedText(key, fallback, arguments), fileName);

    private LocalizedInvalidOperationException CreateLocalizedInvalidOperationException(
        string key,
        string fallback,
        params (string Name, object? Value)[] arguments) =>
        new(() => FormatLocalizedText(key, fallback, arguments));

    private LocalizedInvalidOperationException CreateLocalizedInvalidOperationException(
        Exception innerException,
        string key,
        string fallback,
        params (string Name, object? Value)[] arguments) =>
        new(() => FormatLocalizedText(key, fallback, arguments), innerException);

    private string FormatLocalizedText(
        string key,
        string fallback,
        params (string Name, object? Value)[] arguments)
    {
        if (arguments.Length == 0)
        {
            return GetLocalizedText(key, fallback);
        }

        var localizedArguments = arguments.ToDictionary(
            argument => argument.Name,
            argument => argument.Value,
            StringComparer.Ordinal);
        return _localizationService.Format(key, localizedArguments, fallback);
    }

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

    private static int? TryGetProcessExitCode(Process process, out Exception? exception)
    {
        try
        {
            exception = null;
            return process.ExitCode;
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            exception = ex;
            return null;
        }
    }
}
