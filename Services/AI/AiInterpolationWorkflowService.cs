using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Vidvix.Core.Interfaces;
using Vidvix.Core.Models;
using Vidvix.Services.FFmpeg;
using Vidvix.Utils;

namespace Vidvix.Services.AI;

public sealed class AiInterpolationWorkflowService : IAiInterpolationWorkflowService
{
    private const string WorkflowSessionsDirectoryName = "WorkflowSessions";
    private const string InterpolationDirectoryName = "Interpolation";
    private static readonly TimeSpan RifeOutputPollInterval = TimeSpan.FromMilliseconds(250);
    private readonly ApplicationConfiguration _configuration;
    private readonly IAiRuntimeCatalogService _aiRuntimeCatalogService;
    private readonly IMediaInfoService _mediaInfoService;
    private readonly IFFmpegRuntimeService _ffmpegRuntimeService;
    private readonly IFFmpegService _ffmpegService;
    private readonly ILocalizationService? _localizationService;
    private readonly ILogger _logger;
    private readonly AiPreparedModelCache _preparedModelCache;

    public AiInterpolationWorkflowService(
        ApplicationConfiguration configuration,
        IAiRuntimeCatalogService aiRuntimeCatalogService,
        IMediaInfoService mediaInfoService,
        IFFmpegRuntimeService ffmpegRuntimeService,
        IFFmpegService ffmpegService,
        ILocalizationService? localizationService,
        ILogger logger)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _aiRuntimeCatalogService = aiRuntimeCatalogService ?? throw new ArgumentNullException(nameof(aiRuntimeCatalogService));
        _mediaInfoService = mediaInfoService ?? throw new ArgumentNullException(nameof(mediaInfoService));
        _ffmpegRuntimeService = ffmpegRuntimeService ?? throw new ArgumentNullException(nameof(ffmpegRuntimeService));
        _ffmpegService = ffmpegService ?? throw new ArgumentNullException(nameof(ffmpegService));
        _localizationService = localizationService;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _preparedModelCache = new AiPreparedModelCache(_configuration, _logger);
    }

    public async Task<AiInterpolationResult> InterpolateAsync(
        AiInterpolationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var normalizedInputPath = GetFullPathOrThrow(
            request.InputPath,
            "ai.interpolation.failure.invalidInput",
            "输入文件路径无效。");
        if (!File.Exists(normalizedInputPath))
        {
            throw new AiInterpolationWorkflowException(
                AiInterpolationFailureKind.InvalidInput,
                FormatLocalizedText(
                    "ai.interpolation.failure.inputMissing",
                    "输入视频不存在：{path}",
                    ("path", normalizedInputPath)));
        }

        var ffmpegRuntime = await _ffmpegRuntimeService.EnsureAvailableAsync(cancellationToken).ConfigureAwait(false);
        var runtimeCatalog = await _aiRuntimeCatalogService.GetCatalogAsync(cancellationToken).ConfigureAwait(false);
        var rifeDescriptor = runtimeCatalog.Rife;
        var mediaDetails = await LoadAndValidateMediaDetailsAsync(normalizedInputPath, cancellationToken).ConfigureAwait(false);
        var executionDevice = ResolveExecutionDevice(rifeDescriptor, request.DevicePreference);
        var sourceDuration = mediaDetails.MediaDuration;
        var selectedModel = rifeDescriptor.Models.FirstOrDefault()
            ?? throw new AiInterpolationWorkflowException(
                AiInterpolationFailureKind.RuntimeMissing,
                GetLocalizedText("ai.interpolation.failure.runtimeModelMissing", "RIFE model descriptor is missing."));
        var preparedModelPath = await _preparedModelCache
            .EnsurePreparedModelDirectoryAsync(rifeDescriptor, selectedModel, cancellationToken)
            .ConfigureAwait(false);

        var outputDirectory = string.IsNullOrWhiteSpace(request.OutputDirectory)
            ? mediaDetails.InputDirectory
            : GetFullPathOrThrow(
                request.OutputDirectory,
                "ai.interpolation.failure.invalidOutputDirectory",
                "输出目录无效。");
        Directory.CreateDirectory(outputDirectory);

        var plannedOutputPath = MediaPathResolver.CreateOutputPathWithFileName(
            normalizedInputPath,
            request.OutputFormat.Extension,
            outputDirectory,
            request.OutputFileNameWithoutExtension);
        var outputPath = MediaPathResolver.CreateUniqueOutputPath(plannedOutputPath);
        var sessionRootPath = CreateSessionRootPath();
        Directory.CreateDirectory(sessionRootPath);

        var workflowStartedAt = Stopwatch.StartNew();
        try
        {
            ReportProgress(
                request.Progress,
                AiInterpolationProgressStage.PreparingInput,
                GetLocalizedText("ai.interpolation.progress.stage.prepare", "准备补帧任务"),
                FormatLocalizedText(
                    "ai.interpolation.progress.detail.prepare",
                    "正在校验输入、运行时和输出路径…",
                    Array.Empty<(string Name, object? Value)>()),
                0.02d);

            var inputFramesDirectory = Path.Combine(sessionRootPath, "input-frames");
            Directory.CreateDirectory(inputFramesDirectory);

            await ExtractFramesAsync(
                    ffmpegRuntime.ExecutablePath,
                    normalizedInputPath,
                    inputFramesDirectory,
                    sourceDuration,
                    request.Progress,
                    cancellationToken)
                .ConfigureAwait(false);

            var extractedFrameCount = CountGeneratedFrames(inputFramesDirectory);
            if (extractedFrameCount < 2)
            {
                throw new AiInterpolationWorkflowException(
                    AiInterpolationFailureKind.InvalidInput,
                    GetLocalizedText(
                        "ai.interpolation.failure.insufficientFrames",
                        "补帧至少需要包含两个或以上视频帧。"));
            }

            var sourceFrameRate = ResolveSourceFrameRate(mediaDetails, extractedFrameCount);
            var interpolationPassCount = request.ScaleFactor == AiInterpolationScaleFactor.X4 ? 2 : 1;
            var currentInputDirectory = inputFramesDirectory;
            var currentInputFrameCount = extractedFrameCount;
            string currentOutputDirectory = string.Empty;

            for (var passIndex = 0; passIndex < interpolationPassCount; passIndex++)
            {
                currentOutputDirectory = Path.Combine(sessionRootPath, $"interpolated-pass-{passIndex + 1:00}");
                Directory.CreateDirectory(currentOutputDirectory);

                var expectedFrameCount = currentInputFrameCount * 2;
                var range = ResolveInterpolationProgressRange(passIndex, interpolationPassCount);
                await ExecuteRifePassAsync(
                        rifeDescriptor,
                        preparedModelPath,
                        currentInputDirectory,
                        currentOutputDirectory,
                        executionDevice,
                        request.EnableUhdMode,
                        expectedFrameCount,
                        passIndex,
                        interpolationPassCount,
                        request.Progress,
                        range.Start,
                        range.End,
                        cancellationToken)
                    .ConfigureAwait(false);

                currentInputDirectory = currentOutputDirectory;
                currentInputFrameCount = CountGeneratedFrames(currentOutputDirectory);
            }

            var outputFrameCount = CountGeneratedFrames(currentOutputDirectory);
            if (outputFrameCount < extractedFrameCount * (int)request.ScaleFactor)
            {
                throw new AiInterpolationWorkflowException(
                    AiInterpolationFailureKind.ExecutionFailed,
                    GetLocalizedText(
                        "ai.interpolation.failure.outputFramesIncomplete",
                        "补帧输出帧数量异常，未生成完整的视频帧序列。"));
            }

            var targetFrameRate = sourceFrameRate * (int)request.ScaleFactor;
            var encodeResult = await EncodeOutputAsync(
                    ffmpegRuntime.ExecutablePath,
                    currentOutputDirectory,
                    normalizedInputPath,
                    outputPath,
                    request.OutputFormat.Extension,
                    mediaDetails.HasAudioStream,
                    sourceDuration,
                    targetFrameRate,
                    request.Progress,
                    cancellationToken)
                .ConfigureAwait(false);

            ReportProgress(
                request.Progress,
                AiInterpolationProgressStage.Completed,
                GetLocalizedText("ai.interpolation.progress.stage.complete", "补帧完成"),
                FormatLocalizedText(
                    "ai.interpolation.progress.detail.complete",
                    "输出文件已生成：{fileName}",
                    ("fileName", Path.GetFileName(outputPath))),
                1d,
                isCompleted: true);

            return new AiInterpolationResult
            {
                InputPath = normalizedInputPath,
                OutputPath = outputPath,
                OutputDirectory = Path.GetDirectoryName(outputPath) ?? outputDirectory,
                OutputFileName = Path.GetFileName(outputPath),
                OutputFormat = request.OutputFormat,
                ScaleFactor = request.ScaleFactor,
                ExecutionDeviceKind = executionDevice.Kind,
                ExecutionDeviceDisplayName = executionDevice.DisplayName,
                SourceFrameRate = sourceFrameRate,
                TargetFrameRate = targetFrameRate,
                SourceDuration = sourceDuration,
                WorkflowDuration = workflowStartedAt.Elapsed,
                ExtractedFrameCount = extractedFrameCount,
                OutputFrameCount = outputFrameCount,
                InterpolationPassCount = interpolationPassCount,
                UsedUhdMode = request.EnableUhdMode,
                PreservedOriginalAudio = mediaDetails.HasAudioStream,
                AudioWasTranscoded = encodeResult.AudioWasTranscoded
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (AiInterpolationWorkflowException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.Log(LogLevel.Error, "执行 AI 补帧工作流时发生未处理异常。", exception);
            throw new AiInterpolationWorkflowException(
                AiInterpolationFailureKind.ExecutionFailed,
                GetLocalizedText("ai.interpolation.failure.unexpected", "补帧执行失败，请重试。"),
                exception);
        }
        finally
        {
            workflowStartedAt.Stop();
            TryDeleteDirectory(sessionRootPath);
        }
    }

    private async Task<AiInterpolationMediaDetails> LoadAndValidateMediaDetailsAsync(
        string inputPath,
        CancellationToken cancellationToken)
    {
        var detailsResult = await _mediaInfoService.GetMediaDetailsAsync(inputPath, cancellationToken).ConfigureAwait(false);
        var snapshot = detailsResult.Snapshot;
        if (snapshot is null || !detailsResult.IsSuccess)
        {
            throw new AiInterpolationWorkflowException(
                AiInterpolationFailureKind.InvalidInput,
                GetLocalizedText(
                    "ai.interpolation.failure.mediaInfo",
                    "无法读取输入视频的媒体信息。"));
        }

        var validatedSnapshot = snapshot;

        if (!validatedSnapshot.HasVideoStream)
        {
            throw new AiInterpolationWorkflowException(
                AiInterpolationFailureKind.InvalidInput,
                GetLocalizedText(
                    "ai.interpolation.failure.noVideoStream",
                    "当前输入不包含可补帧的视频轨道。"));
        }

        var inputDirectory = Path.GetDirectoryName(validatedSnapshot.InputPath);
        if (string.IsNullOrWhiteSpace(inputDirectory))
        {
            throw new AiInterpolationWorkflowException(
                AiInterpolationFailureKind.InvalidInput,
                GetLocalizedText(
                    "ai.interpolation.failure.invalidInput",
                    "输入文件路径无效。"));
        }

        return new AiInterpolationMediaDetails(
            validatedSnapshot.InputPath,
            inputDirectory,
            validatedSnapshot.MediaDuration ?? TimeSpan.Zero,
            validatedSnapshot.PrimaryVideoFrameRate,
            validatedSnapshot.HasAudioStream);
    }

    private ExecutionDeviceResolution ResolveExecutionDevice(
        AiRuntimeDescriptor descriptor,
        AiInterpolationDevicePreference devicePreference)
    {
        if (!descriptor.IsAvailable)
        {
            throw new AiInterpolationWorkflowException(
                AiInterpolationFailureKind.RuntimeMissing,
                string.IsNullOrWhiteSpace(descriptor.AvailabilityReason)
                    ? GetLocalizedText("ai.interpolation.failure.runtimeMissing", "RIFE runtime 缺失或目录不完整。")
                    : descriptor.AvailabilityReason);
        }

        if (devicePreference == AiInterpolationDevicePreference.Cpu)
        {
            if (descriptor.CpuSupport.State is AiExecutionSupportState.Unavailable or AiExecutionSupportState.Unsupported)
            {
                throw CreateDeviceUnavailableException(descriptor.CpuSupport, allowRuntimeMissing: false);
            }

            return new ExecutionDeviceResolution(
                AiInterpolationExecutionDeviceKind.Cpu,
                GetLocalizedText("ai.interpolation.deviceOption.cpu", "CPU"),
                UseCpuFallback: true,
                GpuIndex: null);
        }

        var preferredGpuDevice = ResolvePreferredGpuDevice(descriptor);
        if (preferredGpuDevice is not null)
        {
            return new ExecutionDeviceResolution(
                AiInterpolationExecutionDeviceKind.Gpu,
                preferredGpuDevice.Name,
                UseCpuFallback: false,
                GpuIndex: preferredGpuDevice.Index);
        }

        if (descriptor.GpuSupport.State is AiExecutionSupportState.Pending or
            AiExecutionSupportState.Available or
            AiExecutionSupportState.ProbeFailed)
        {
            return new ExecutionDeviceResolution(
                AiInterpolationExecutionDeviceKind.Gpu,
                GetLocalizedText("ai.interpolation.device.gpu", "GPU"),
                UseCpuFallback: false,
                GpuIndex: null);
        }

        if (descriptor.CpuSupport.IsAvailable)
        {
            return new ExecutionDeviceResolution(
                AiInterpolationExecutionDeviceKind.Cpu,
                GetLocalizedText("ai.interpolation.device.cpuFallback", "CPU fallback"),
                UseCpuFallback: true,
                GpuIndex: null);
        }

        throw CreateDeviceUnavailableException(descriptor.GpuSupport, allowRuntimeMissing: true);
    }

    private static AiRuntimeGpuDeviceDescriptor? ResolvePreferredGpuDevice(AiRuntimeDescriptor descriptor) =>
        descriptor.GpuDevices
            .Where(device => device.IsAvailable)
            .OrderByDescending(device => AiGpuDeviceClassifier.GetPriority(device.Kind))
            .ThenBy(device => device.Index)
            .FirstOrDefault();

    private AiInterpolationWorkflowException CreateDeviceUnavailableException(
        AiExecutionSupportStatus status,
        bool allowRuntimeMissing)
    {
        var failureKind = allowRuntimeMissing && status.State == AiExecutionSupportState.MissingRuntime
            ? AiInterpolationFailureKind.RuntimeMissing
            : AiInterpolationFailureKind.DeviceUnavailable;
        var message = !string.IsNullOrWhiteSpace(status.DiagnosticMessage)
            ? status.DiagnosticMessage
            : GetLocalizedText(
                "ai.interpolation.failure.deviceUnavailable",
                "当前机器无法使用所选的补帧设备策略。");
        return new AiInterpolationWorkflowException(failureKind, message);
    }

    private async Task ExtractFramesAsync(
        string ffmpegExecutablePath,
        string inputPath,
        string outputDirectory,
        TimeSpan sourceDuration,
        IProgress<AiInterpolationProgress>? progress,
        CancellationToken cancellationToken)
    {
        ReportProgress(
            progress,
            AiInterpolationProgressStage.ExtractingFrames,
            GetLocalizedText("ai.interpolation.progress.stage.extract", "抽取视频帧"),
            GetLocalizedText("ai.interpolation.progress.detail.extract.start", "正在将源视频解包为逐帧图片…"),
            0.05d);

        var command = new FFmpegCommand(
            ffmpegExecutablePath,
            new[]
            {
                "-hide_banner",
                "-y",
                "-i",
                inputPath,
                "-map",
                "0:v:0",
                "-fps_mode",
                "passthrough",
                Path.Combine(outputDirectory, "%08d.png")
            });

        var result = await _ffmpegService.ExecuteAsync(
                command,
                new FFmpegExecutionOptions
                {
                    InputDuration = sourceDuration > TimeSpan.Zero ? sourceDuration : null,
                    Progress = CreateFfmpegProgressBridge(
                        progress,
                        0.05d,
                        0.26d,
                        GetLocalizedText("ai.interpolation.progress.stage.extract", "抽取视频帧"),
                        update => update.ProcessedDuration is { } processed
                            ? FormatLocalizedText(
                                "ai.interpolation.progress.detail.extract.duration",
                                "已抽取到视频时间：{time}",
                                ("time", processed.ToString(@"hh\:mm\:ss\.ff", CultureInfo.InvariantCulture)))
                            : GetLocalizedText(
                                "ai.interpolation.progress.detail.extract.running",
                                "正在持续抽取视频帧…"))
                },
                cancellationToken)
            .ConfigureAwait(false);

        ThrowIfFfmpegFailed(result, AiInterpolationFailureKind.ExecutionFailed, "抽取视频帧失败。");
    }

    private async Task<RifePassResult> EncodeOutputAsync(
        string ffmpegExecutablePath,
        string inputFramesDirectory,
        string sourceInputPath,
        string outputPath,
        string outputExtension,
        bool preserveOriginalAudio,
        TimeSpan sourceDuration,
        double targetFrameRate,
        IProgress<AiInterpolationProgress>? progress,
        CancellationToken cancellationToken)
    {
        ReportProgress(
            progress,
            AiInterpolationProgressStage.EncodingOutput,
            GetLocalizedText("ai.interpolation.progress.stage.encode", "生成输出视频"),
            GetLocalizedText("ai.interpolation.progress.detail.encode.start", "正在编码补帧视频并回填原音轨…"),
            0.82d);

        var copyAudioResult = await ExecuteEncodePassAsync(
                ffmpegExecutablePath,
                inputFramesDirectory,
                sourceInputPath,
                outputPath,
                outputExtension,
                preserveOriginalAudio,
                transcodeAudio: false,
                sourceDuration,
                targetFrameRate,
                progress,
                cancellationToken)
            .ConfigureAwait(false);

        if (copyAudioResult.Result.WasSuccessful)
        {
            return new RifePassResult(copyAudioResult.OutputPath, AudioWasTranscoded: false);
        }

        if (!preserveOriginalAudio)
        {
            ThrowIfFfmpegFailed(copyAudioResult.Result, AiInterpolationFailureKind.ExecutionFailed, "编码补帧视频失败。");
        }

        _logger.Log(
            LogLevel.Warning,
            $"补帧输出在复制原音轨时失败，正在回退为 {AiOutputEncodingPolicy.GetAudioFallbackCodecDisplayName(outputExtension)} 音频转码。");

        var fallbackResult = await ExecuteEncodePassAsync(
                ffmpegExecutablePath,
                inputFramesDirectory,
                sourceInputPath,
                outputPath,
                outputExtension,
                preserveOriginalAudio,
                transcodeAudio: true,
                sourceDuration,
                targetFrameRate,
                progress,
                cancellationToken)
            .ConfigureAwait(false);

        ThrowIfFfmpegFailed(fallbackResult.Result, AiInterpolationFailureKind.ExecutionFailed, "编码补帧视频失败。");
        return new RifePassResult(fallbackResult.OutputPath, AudioWasTranscoded: true);
    }

    private async Task<EncodeExecutionResult> ExecuteEncodePassAsync(
        string ffmpegExecutablePath,
        string inputFramesDirectory,
        string sourceInputPath,
        string outputPath,
        string outputExtension,
        bool preserveOriginalAudio,
        bool transcodeAudio,
        TimeSpan sourceDuration,
        double targetFrameRate,
        IProgress<AiInterpolationProgress>? progress,
        CancellationToken cancellationToken)
    {
        var temporaryOutputPath = transcodeAudio
            ? Path.Combine(
                Path.GetDirectoryName(outputPath) ?? Path.GetTempPath(),
                $"{Path.GetFileNameWithoutExtension(outputPath)}_audiofallback{Path.GetExtension(outputPath)}")
            : outputPath;
        if (File.Exists(temporaryOutputPath))
        {
            File.Delete(temporaryOutputPath);
        }

        var arguments = new List<string>
        {
            "-hide_banner",
            "-y",
            "-framerate",
            targetFrameRate.ToString("0.###", CultureInfo.InvariantCulture),
            "-i",
            Path.Combine(inputFramesDirectory, "%08d.png")
        };

        if (preserveOriginalAudio)
        {
            arguments.Add("-i");
            arguments.Add(sourceInputPath);
        }

        arguments.Add("-map");
        arguments.Add("0:v:0");

        if (preserveOriginalAudio)
        {
            arguments.Add("-map");
            arguments.Add("1:a?");
        }

        AiOutputEncodingPolicy.ApplyEncoding(arguments, outputExtension, preserveOriginalAudio, transcodeAudio);

        if (preserveOriginalAudio)
        {
            arguments.Add("-shortest");
        }

        arguments.Add(temporaryOutputPath);

        var result = await _ffmpegService.ExecuteAsync(
                new FFmpegCommand(ffmpegExecutablePath, arguments),
                new FFmpegExecutionOptions
                {
                    InputDuration = sourceDuration > TimeSpan.Zero ? sourceDuration : null,
                    Progress = CreateFfmpegProgressBridge(
                        progress,
                        0.82d,
                        0.98d,
                        GetLocalizedText("ai.interpolation.progress.stage.encode", "生成输出视频"),
                        update => update.ProcessedDuration is { } processed
                            ? FormatLocalizedText(
                                "ai.interpolation.progress.detail.encode.duration",
                                "已编码到视频时间：{time}",
                                ("time", processed.ToString(@"hh\:mm\:ss\.ff", CultureInfo.InvariantCulture)))
                            : GetLocalizedText(
                                "ai.interpolation.progress.detail.encode.running",
                                "正在编码最终视频…"))
                },
                cancellationToken)
            .ConfigureAwait(false);

        if (!result.WasSuccessful && transcodeAudio && File.Exists(temporaryOutputPath))
        {
            File.Delete(temporaryOutputPath);
        }

        if (!transcodeAudio || !result.WasSuccessful)
        {
            return new EncodeExecutionResult(temporaryOutputPath, result);
        }

        if (File.Exists(outputPath))
        {
            File.Delete(outputPath);
        }

        File.Move(temporaryOutputPath, outputPath);
        return new EncodeExecutionResult(outputPath, result);
    }

    private async Task ExecuteRifePassAsync(
        AiRuntimeDescriptor descriptor,
        string stagedModelPath,
        string inputDirectory,
        string outputDirectory,
        ExecutionDeviceResolution executionDevice,
        bool enableUhdMode,
        int expectedFrameCount,
        int passIndex,
        int passCount,
        IProgress<AiInterpolationProgress>? progress,
        double progressStart,
        double progressEnd,
        CancellationToken cancellationToken)
    {
        var passDisplayIndex = passIndex + 1;
        var stageTitle = GetLocalizedText("ai.interpolation.progress.stage.interpolate", "运行 RIFE 补帧");
        var initialDetail = FormatLocalizedText(
            "ai.interpolation.progress.detail.interpolate.start",
            "正在执行第 {currentPass}/{totalPasses} 次补帧…",
            ("currentPass", passDisplayIndex),
            ("totalPasses", passCount));
        ReportProgress(progress, AiInterpolationProgressStage.InterpolatingFrames, stageTitle, initialDetail, progressStart);

        var arguments = new List<string>
        {
            "-i",
            inputDirectory,
            "-o",
            outputDirectory,
            "-m",
            stagedModelPath
        };

        if (executionDevice.UseCpuFallback)
        {
            arguments.Add("-g");
            arguments.Add("-1");
        }
        else if (executionDevice.GpuIndex is int gpuIndex)
        {
            arguments.Add("-g");
            arguments.Add(gpuIndex.ToString(CultureInfo.InvariantCulture));
        }

        if (enableUhdMode)
        {
            arguments.Add("-u");
        }

        var result = await ExecuteRifeProcessAsync(
                descriptor.ExecutablePath,
                arguments,
                outputDirectory,
                expectedFrameCount,
                stageTitle,
                passDisplayIndex,
                passCount,
                progress,
                progressStart,
                progressEnd,
                cancellationToken)
            .ConfigureAwait(false);

        if (result.ExitCode != 0)
        {
            throw new AiInterpolationWorkflowException(
                AiInterpolationFailureKind.ExecutionFailed,
                SummarizeProcessFailure(
                    result.StandardOutput,
                    result.StandardError,
                    GetLocalizedText("ai.interpolation.failure.rifeExecution", "RIFE 补帧执行失败。")));
        }

        if (CountGeneratedFrames(outputDirectory) < expectedFrameCount)
        {
            throw new AiInterpolationWorkflowException(
                AiInterpolationFailureKind.ExecutionFailed,
                GetLocalizedText(
                    "ai.interpolation.failure.outputFramesIncomplete",
                    "补帧输出帧数量异常，未生成完整的视频帧序列。"));
        }
    }

    private async Task<RifeProcessExecutionResult> ExecuteRifeProcessAsync(
        string executablePath,
        IReadOnlyList<string> arguments,
        string monitoredOutputDirectory,
        int expectedFrameCount,
        string stageTitle,
        int passIndex,
        int passCount,
        IProgress<AiInterpolationProgress>? progress,
        double progressStart,
        double progressEnd,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(executablePath)
        {
            WorkingDirectory = ResolveProcessWorkingDirectory(executablePath, monitoredOutputDirectory),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process
        {
            StartInfo = startInfo,
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
        };

        process.ErrorDataReceived += (_, eventArgs) =>
        {
            if (eventArgs.Data is null)
            {
                standardErrorClosed.TrySetResult();
                return;
            }

            standardErrorBuilder.AppendLine(eventArgs.Data);
        };

        if (!process.Start())
        {
            throw new AiInterpolationWorkflowException(
                AiInterpolationFailureKind.ExecutionFailed,
                GetLocalizedText("ai.interpolation.failure.rifeStart", "RIFE 进程启动失败。"));
        }

        TryReduceProcessPriority(process, ProcessPriorityClass.BelowNormal);
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        var processId = process.Id;
        using var cancellationRegistration = ExternalProcessTermination.RegisterTermination(process, cancellationToken);
        using var monitorCancellationSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var monitorTask = MonitorRifeOutputDirectoryAsync(
            monitoredOutputDirectory,
            expectedFrameCount,
            stageTitle,
            passIndex,
            passCount,
            progress,
            progressStart,
            progressEnd,
            process,
            monitorCancellationSource.Token);

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
                    "RIFE 取消后在宽限时间内仍未完全退出，已继续返回结果。")
                .ConfigureAwait(false);
            throw;
        }
        finally
        {
            monitorCancellationSource.Cancel();
            try
            {
                await monitorTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (monitorCancellationSource.IsCancellationRequested)
            {
            }

            await Task.WhenAll(standardOutputClosed.Task, standardErrorClosed.Task).ConfigureAwait(false);
        }

        return new RifeProcessExecutionResult(
            process.ExitCode,
            standardOutputBuilder.ToString(),
            standardErrorBuilder.ToString());
    }

    private async Task MonitorRifeOutputDirectoryAsync(
        string outputDirectory,
        int expectedFrameCount,
        string stageTitle,
        int passIndex,
        int passCount,
        IProgress<AiInterpolationProgress>? progress,
        double progressStart,
        double progressEnd,
        Process process,
        CancellationToken cancellationToken)
    {
        if (progress is null)
        {
            return;
        }

        while (!process.HasExited && !cancellationToken.IsCancellationRequested)
        {
            var generatedFrameCount = CountGeneratedFrames(outputDirectory);
            var stageRatio = expectedFrameCount > 0
                ? Math.Clamp(generatedFrameCount / (double)expectedFrameCount, 0d, 0.98d)
                : 0d;
            var overallRatio = progressStart + ((progressEnd - progressStart) * stageRatio);
            var detail = FormatLocalizedText(
                "ai.interpolation.progress.detail.interpolate.running",
                "第 {currentPass}/{totalPasses} 次补帧：已生成 {currentFrames}/{expectedFrames} 帧",
                ("currentPass", passIndex),
                ("totalPasses", passCount),
                ("currentFrames", generatedFrameCount),
                ("expectedFrames", expectedFrameCount));
            ReportProgress(
                progress,
                AiInterpolationProgressStage.InterpolatingFrames,
                stageTitle,
                detail,
                overallRatio);

            await Task.Delay(RifeOutputPollInterval, cancellationToken).ConfigureAwait(false);
        }
    }

    private string StageRifeModelAssets(string sessionRootPath, AiRuntimeDescriptor descriptor)
    {
        var model = descriptor.Models.FirstOrDefault()
            ?? throw new AiInterpolationWorkflowException(
                AiInterpolationFailureKind.RuntimeMissing,
                GetLocalizedText("ai.interpolation.failure.runtimeModelMissing", "RIFE 模型描述缺失。"));
        var targetDirectoryPath = Path.Combine(sessionRootPath, "staged-models", model.PreparedDirectoryName);
        Directory.CreateDirectory(targetDirectoryPath);

        foreach (var asset in model.Assets)
        {
            File.Copy(asset.ConfigPath, Path.Combine(targetDirectoryPath, Path.GetFileName(asset.ConfigPath)), overwrite: true);
            File.Copy(asset.WeightPath, Path.Combine(targetDirectoryPath, Path.GetFileName(asset.WeightPath)), overwrite: true);
        }

        return targetDirectoryPath;
    }

    private static int CountGeneratedFrames(string directoryPath)
    {
        if (!Directory.Exists(directoryPath))
        {
            return 0;
        }

        return Directory.EnumerateFiles(directoryPath, "*.png", SearchOption.TopDirectoryOnly).Count();
    }

    private static (double Start, double End) ResolveInterpolationProgressRange(int passIndex, int passCount)
    {
        const double interpolationStart = 0.26d;
        const double interpolationEnd = 0.82d;

        var totalRange = interpolationEnd - interpolationStart;
        var perPassRange = totalRange / Math.Max(passCount, 1);
        var start = interpolationStart + (perPassRange * passIndex);
        return (start, start + perPassRange);
    }

    private IProgress<FFmpegProgressUpdate> CreateFfmpegProgressBridge(
        IProgress<AiInterpolationProgress>? progress,
        double progressStart,
        double progressEnd,
        string stageTitle,
        Func<FFmpegProgressUpdate, string> detailFactory)
    {
        return new Progress<FFmpegProgressUpdate>(update =>
        {
            if (progress is null)
            {
                return;
            }

            var stageRatio = update.ProgressRatio ?? (update.IsCompleted ? 1d : 0d);
            var clampedRatio = Math.Clamp(stageRatio, 0d, 1d);
            var overallRatio = progressStart + ((progressEnd - progressStart) * clampedRatio);
            ReportProgress(
                progress,
                update.IsCompleted
                    ? AiInterpolationProgressStage.EncodingOutput
                    : overallRatio < 0.3d
                        ? AiInterpolationProgressStage.ExtractingFrames
                        : AiInterpolationProgressStage.EncodingOutput,
                stageTitle,
                detailFactory(update),
                overallRatio,
                isCompleted: update.IsCompleted && progressEnd >= 0.98d);
        });
    }

    private void ThrowIfFfmpegFailed(
        FFmpegExecutionResult result,
        AiInterpolationFailureKind failureKind,
        string fallbackMessage)
    {
        if (result.WasCancelled)
        {
            throw new OperationCanceledException(fallbackMessage);
        }

        if (result.WasSuccessful)
        {
            return;
        }

        throw new AiInterpolationWorkflowException(
            failureKind,
            SummarizeProcessFailure(result.StandardOutput, result.StandardError, fallbackMessage));
    }

    private string SummarizeProcessFailure(
        string standardOutput,
        string standardError,
        string fallbackMessage)
    {
        var mergedOutput = string.IsNullOrWhiteSpace(standardError)
            ? standardOutput
            : string.IsNullOrWhiteSpace(standardOutput)
                ? standardError
                : $"{standardOutput}{Environment.NewLine}{standardError}";

        var summary = mergedOutput
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Take(4)
            .ToArray();

        return summary.Length == 0
            ? fallbackMessage
            : string.Join(" | ", summary);
    }

    private double ResolveSourceFrameRate(AiInterpolationMediaDetails mediaDetails, int extractedFrameCount)
    {
        if (mediaDetails.PrimaryVideoFrameRate is > 0d)
        {
            return mediaDetails.PrimaryVideoFrameRate.Value;
        }

        if (mediaDetails.MediaDuration > TimeSpan.Zero && extractedFrameCount > 0)
        {
            var estimatedFrameRate = extractedFrameCount / mediaDetails.MediaDuration.TotalSeconds;
            if (estimatedFrameRate > 0d)
            {
                return estimatedFrameRate;
            }
        }

        return 30d;
    }

    private string CreateSessionRootPath() =>
        MutableRuntimeStorage.GetLocalStorageRootPath(
            _configuration.LocalDataDirectoryName,
            _configuration.RuntimeDirectoryName,
            _configuration.AiRuntimeDirectoryName,
            WorkflowSessionsDirectoryName,
            InterpolationDirectoryName,
            Guid.NewGuid().ToString("N"));

    private static string ResolveProcessWorkingDirectory(string executablePath, string preferredWorkingDirectory)
    {
        if (!string.IsNullOrWhiteSpace(preferredWorkingDirectory))
        {
            Directory.CreateDirectory(preferredWorkingDirectory);
            return Path.GetFullPath(preferredWorkingDirectory);
        }

        return Path.GetDirectoryName(executablePath) ?? AppContext.BaseDirectory;
    }

    private static string GetFullPathOrThrow(string? path, string key, string fallback)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new AiInterpolationWorkflowException(AiInterpolationFailureKind.InvalidInput, fallback);
        }

        try
        {
            return Path.GetFullPath(path);
        }
        catch (Exception exception) when (
            exception is ArgumentException or
            NotSupportedException or
            PathTooLongException)
        {
            throw new AiInterpolationWorkflowException(AiInterpolationFailureKind.InvalidInput, fallback, exception);
        }
    }

    private void ReportProgress(
        IProgress<AiInterpolationProgress>? progress,
        AiInterpolationProgressStage stage,
        string stageTitle,
        string detailText,
        double? progressRatio,
        bool isCompleted = false)
    {
        progress?.Report(
            new AiInterpolationProgress(
                stage,
                stageTitle,
                detailText,
                progressRatio,
                isCompleted));
    }

    private string GetLocalizedText(string key, string fallback) =>
        _localizationService?.GetString(key, fallback) ?? fallback;

    private string FormatLocalizedText(string key, string fallback, params (string Name, object? Value)[] arguments)
    {
        if (_localizationService is null || arguments.Length == 0)
        {
            return fallback;
        }

        var localizedArguments = new Dictionary<string, object?>(arguments.Length, StringComparer.Ordinal);
        foreach (var argument in arguments)
        {
            localizedArguments[argument.Name] = argument.Value;
        }

        return _localizationService.Format(key, localizedArguments, fallback);
    }

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

    private static void TryReduceProcessPriority(Process process, ProcessPriorityClass priorityClass)
    {
        try
        {
            if (!process.HasExited)
            {
                process.PriorityClass = priorityClass;
            }
        }
        catch
        {
        }
    }

    private sealed record AiInterpolationMediaDetails(
        string InputPath,
        string InputDirectory,
        TimeSpan MediaDuration,
        double? PrimaryVideoFrameRate,
        bool HasAudioStream);

    private sealed record ExecutionDeviceResolution(
        AiInterpolationExecutionDeviceKind Kind,
        string DisplayName,
        bool UseCpuFallback,
        int? GpuIndex);

    private sealed record EncodeExecutionResult(
        string OutputPath,
        FFmpegExecutionResult Result);

    private sealed record RifePassResult(
        string OutputPath,
        bool AudioWasTranscoded);

    private sealed record RifeProcessExecutionResult(
        int ExitCode,
        string StandardOutput,
        string StandardError);
}
