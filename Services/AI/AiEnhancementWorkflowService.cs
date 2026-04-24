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

public sealed class AiEnhancementWorkflowService : IAiEnhancementWorkflowService
{
    private const string WorkflowSessionsDirectoryName = "WorkflowSessions";
    private const string EnhancementDirectoryName = "Enhancement";
    private static readonly TimeSpan RealEsrganOutputPollInterval = TimeSpan.FromMilliseconds(250);
    private readonly ApplicationConfiguration _configuration;
    private readonly IAiRuntimeCatalogService _aiRuntimeCatalogService;
    private readonly IMediaInfoService _mediaInfoService;
    private readonly IFFmpegRuntimeService _ffmpegRuntimeService;
    private readonly IFFmpegService _ffmpegService;
    private readonly ILocalizationService? _localizationService;
    private readonly ILogger _logger;

    public AiEnhancementWorkflowService(
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
    }

    public async Task<AiEnhancementResult> EnhanceAsync(
        AiEnhancementRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var normalizedInputPath = GetFullPathOrThrow(
            request.InputPath,
            GetLocalizedText("ai.enhancement.failure.invalidInput", "输入文件路径无效。"));
        if (!File.Exists(normalizedInputPath))
        {
            throw new AiEnhancementWorkflowException(
                AiEnhancementFailureKind.InvalidInput,
                FormatLocalizedText(
                    "ai.enhancement.failure.inputMissing",
                    "输入视频不存在：{path}",
                    ("path", normalizedInputPath)));
        }

        var ffmpegRuntime = await _ffmpegRuntimeService.EnsureAvailableAsync(cancellationToken).ConfigureAwait(false);
        var runtimeCatalog = await _aiRuntimeCatalogService.GetCatalogAsync(cancellationToken).ConfigureAwait(false);
        var realEsrganDescriptor = runtimeCatalog.RealEsrgan;
        var selectedModel = ResolveSelectedModel(realEsrganDescriptor, request.ModelTier);
        var mediaDetails = await LoadAndValidateMediaDetailsAsync(normalizedInputPath, cancellationToken).ConfigureAwait(false);
        var executionDevice = ResolveExecutionDevice(realEsrganDescriptor);
        var scalePlan = AiEnhancementScalePlanner.BuildPlan(selectedModel.NativeScaleFactors, request.TargetScaleFactor);
        var sourceDuration = mediaDetails.MediaDuration;

        var outputDirectory = string.IsNullOrWhiteSpace(request.OutputDirectory)
            ? mediaDetails.InputDirectory
            : GetFullPathOrThrow(
                request.OutputDirectory,
                GetLocalizedText("ai.enhancement.failure.invalidOutputDirectory", "输出目录无效。"));
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
                AiEnhancementProgressStage.PreparingInput,
                GetLocalizedText("ai.enhancement.progress.stage.prepare", "准备增强任务"),
                GetLocalizedText("ai.enhancement.progress.detail.prepare", "正在校验输入、运行时和输出路径…"),
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
            if (extractedFrameCount == 0)
            {
                throw new AiEnhancementWorkflowException(
                    AiEnhancementFailureKind.InvalidInput,
                    GetLocalizedText(
                        "ai.enhancement.failure.noFrames",
                        "增强至少需要包含一帧或以上的视频帧。"));
            }

            var sourceFrameRate = ResolveSourceFrameRate(mediaDetails, extractedFrameCount);
            var stagedModelPath = StageModelAssets(sessionRootPath, selectedModel);
            var currentInputDirectory = inputFramesDirectory;
            var currentInputFrameCount = extractedFrameCount;
            string currentOutputDirectory = string.Empty;

            for (var passIndex = 0; passIndex < scalePlan.PassCount; passIndex++)
            {
                var passScale = scalePlan.PassScales[passIndex];
                currentOutputDirectory = Path.Combine(sessionRootPath, $"enhanced-pass-{passIndex + 1:00}-{passScale}x");
                Directory.CreateDirectory(currentOutputDirectory);

                var range = ResolveEnhancementProgressRange(passIndex, scalePlan.PassCount);
                await ExecuteRealEsrganPassAsync(
                        realEsrganDescriptor,
                        stagedModelPath,
                        currentInputDirectory,
                        currentOutputDirectory,
                        selectedModel.RuntimeModelName,
                        passScale,
                        executionDevice,
                        currentInputFrameCount,
                        passIndex,
                        scalePlan.PassCount,
                        request.Progress,
                        range.Start,
                        range.End,
                        cancellationToken)
                    .ConfigureAwait(false);

                currentInputDirectory = currentOutputDirectory;
                currentInputFrameCount = CountGeneratedFrames(currentOutputDirectory);
            }

            var outputFrameCount = CountGeneratedFrames(currentOutputDirectory);
            if (outputFrameCount < extractedFrameCount)
            {
                throw new AiEnhancementWorkflowException(
                    AiEnhancementFailureKind.ExecutionFailed,
                    GetLocalizedText(
                        "ai.enhancement.failure.outputFramesIncomplete",
                        "增强输出帧数量异常，未生成完整的视频帧序列。"));
            }

            var encodeResult = await EncodeOutputAsync(
                    ffmpegRuntime.ExecutablePath,
                    currentOutputDirectory,
                    normalizedInputPath,
                    outputPath,
                    request.OutputFormat.Extension,
                    mediaDetails.HasAudioStream,
                    sourceDuration,
                    sourceFrameRate,
                    scalePlan,
                    request.Progress,
                    cancellationToken)
                .ConfigureAwait(false);

            ReportProgress(
                request.Progress,
                AiEnhancementProgressStage.Completed,
                GetLocalizedText("ai.enhancement.progress.stage.complete", "增强完成"),
                FormatLocalizedText(
                    "ai.enhancement.progress.detail.complete",
                    "输出文件已生成：{fileName}",
                    ("fileName", Path.GetFileName(outputPath))),
                1d,
                isCompleted: true);

            return new AiEnhancementResult
            {
                InputPath = normalizedInputPath,
                OutputPath = outputPath,
                OutputDirectory = Path.GetDirectoryName(outputPath) ?? outputDirectory,
                OutputFileName = Path.GetFileName(outputPath),
                OutputFormat = request.OutputFormat,
                ModelTier = request.ModelTier,
                ModelDisplayName = selectedModel.DisplayName,
                ScalePlan = scalePlan,
                ExecutionDeviceKind = executionDevice.Kind,
                ExecutionDeviceDisplayName = executionDevice.DisplayName,
                SourceFrameRate = sourceFrameRate,
                SourceDuration = sourceDuration,
                WorkflowDuration = workflowStartedAt.Elapsed,
                ExtractedFrameCount = extractedFrameCount,
                OutputFrameCount = outputFrameCount,
                PreservedOriginalAudio = mediaDetails.HasAudioStream,
                AudioWasTranscoded = encodeResult.AudioWasTranscoded
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (AiEnhancementWorkflowException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.Log(LogLevel.Error, "执行 AI 增强工作流时发生未处理异常。", exception);
            throw new AiEnhancementWorkflowException(
                AiEnhancementFailureKind.ExecutionFailed,
                GetLocalizedText("ai.enhancement.failure.unexpected", "增强执行失败，请重试。"),
                exception);
        }
        finally
        {
            workflowStartedAt.Stop();
            TryDeleteDirectory(sessionRootPath);
        }
    }

    private async Task<AiEnhancementMediaDetails> LoadAndValidateMediaDetailsAsync(
        string inputPath,
        CancellationToken cancellationToken)
    {
        var detailsResult = await _mediaInfoService.GetMediaDetailsAsync(inputPath, cancellationToken).ConfigureAwait(false);
        var snapshot = detailsResult.Snapshot;
        if (snapshot is null || !detailsResult.IsSuccess)
        {
            throw new AiEnhancementWorkflowException(
                AiEnhancementFailureKind.InvalidInput,
                GetLocalizedText(
                    "ai.enhancement.failure.mediaInfo",
                    "无法读取输入视频的媒体信息。"));
        }

        if (!snapshot.HasVideoStream)
        {
            throw new AiEnhancementWorkflowException(
                AiEnhancementFailureKind.InvalidInput,
                GetLocalizedText(
                    "ai.enhancement.failure.noVideoStream",
                    "当前输入不包含可增强的视频轨道。"));
        }

        var inputDirectory = Path.GetDirectoryName(snapshot.InputPath);
        if (string.IsNullOrWhiteSpace(inputDirectory))
        {
            throw new AiEnhancementWorkflowException(
                AiEnhancementFailureKind.InvalidInput,
                GetLocalizedText("ai.enhancement.failure.invalidInput", "输入文件路径无效。"));
        }

        return new AiEnhancementMediaDetails(
            snapshot.InputPath,
            inputDirectory,
            snapshot.MediaDuration ?? TimeSpan.Zero,
            snapshot.PrimaryVideoFrameRate,
            snapshot.HasAudioStream);
    }

    private AiRuntimeModelDescriptor ResolveSelectedModel(
        AiRuntimeDescriptor descriptor,
        AiEnhancementModelTier modelTier)
    {
        if (!descriptor.IsAvailable)
        {
            throw new AiEnhancementWorkflowException(
                AiEnhancementFailureKind.RuntimeMissing,
                string.IsNullOrWhiteSpace(descriptor.AvailabilityReason)
                    ? GetLocalizedText("ai.enhancement.failure.runtimeMissing", "Real-ESRGAN runtime 缺失或目录不完整。")
                    : descriptor.AvailabilityReason);
        }

        var runtimeModelId = modelTier == AiEnhancementModelTier.Standard
            ? "standard"
            : "anime";
        var model = descriptor.Models.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, runtimeModelId, StringComparison.OrdinalIgnoreCase));
        if (model is not null)
        {
            return model;
        }

        throw new AiEnhancementWorkflowException(
            AiEnhancementFailureKind.RuntimeMissing,
            GetLocalizedText("ai.enhancement.failure.runtimeModelMissing", "Real-ESRGAN 模型描述缺失。"));
    }

    private ExecutionDeviceResolution ResolveExecutionDevice(AiRuntimeDescriptor descriptor)
    {
        if (!descriptor.IsAvailable)
        {
            throw new AiEnhancementWorkflowException(
                AiEnhancementFailureKind.RuntimeMissing,
                string.IsNullOrWhiteSpace(descriptor.AvailabilityReason)
                    ? GetLocalizedText("ai.enhancement.failure.runtimeMissing", "Real-ESRGAN runtime 缺失或目录不完整。")
                    : descriptor.AvailabilityReason);
        }

        if (descriptor.GpuSupport.IsAvailable)
        {
            return new ExecutionDeviceResolution(
                AiEnhancementExecutionDeviceKind.Gpu,
                GetLocalizedText("ai.enhancement.device.gpu", "GPU"),
                UseCpuFallback: false);
        }

        if (descriptor.CpuSupport.IsAvailable)
        {
            return new ExecutionDeviceResolution(
                AiEnhancementExecutionDeviceKind.Cpu,
                GetLocalizedText("ai.enhancement.device.cpuFallback", "CPU fallback"),
                UseCpuFallback: true);
        }

        var failureKind =
            descriptor.GpuSupport.State == AiExecutionSupportState.MissingRuntime ||
            descriptor.CpuSupport.State == AiExecutionSupportState.MissingRuntime
                ? AiEnhancementFailureKind.RuntimeMissing
                : AiEnhancementFailureKind.DeviceUnavailable;

        throw new AiEnhancementWorkflowException(
            failureKind,
            BuildDeviceUnavailableMessage(descriptor));
    }

    private string BuildDeviceUnavailableMessage(AiRuntimeDescriptor descriptor)
    {
        var diagnostics = new List<string>(2);
        if (!string.IsNullOrWhiteSpace(descriptor.GpuSupport.DiagnosticMessage))
        {
            diagnostics.Add(
                FormatLocalizedText(
                    "ai.enhancement.failure.deviceUnavailable.gpu",
                    "GPU：{message}",
                    ("message", descriptor.GpuSupport.DiagnosticMessage)));
        }

        if (!string.IsNullOrWhiteSpace(descriptor.CpuSupport.DiagnosticMessage))
        {
            diagnostics.Add(
                FormatLocalizedText(
                    "ai.enhancement.failure.deviceUnavailable.cpu",
                    "CPU fallback：{message}",
                    ("message", descriptor.CpuSupport.DiagnosticMessage)));
        }

        if (diagnostics.Count > 0)
        {
            return string.Join(" | ", diagnostics);
        }

        return GetLocalizedText(
            "ai.enhancement.failure.deviceUnavailable",
            "当前机器无法使用可用的增强执行设备。");
    }

    private async Task ExtractFramesAsync(
        string ffmpegExecutablePath,
        string inputPath,
        string outputDirectory,
        TimeSpan sourceDuration,
        IProgress<AiEnhancementProgress>? progress,
        CancellationToken cancellationToken)
    {
        ReportProgress(
            progress,
            AiEnhancementProgressStage.ExtractingFrames,
            GetLocalizedText("ai.enhancement.progress.stage.extract", "抽取视频帧"),
            GetLocalizedText("ai.enhancement.progress.detail.extract.start", "正在将源视频解包为逐帧图片…"),
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
                        GetLocalizedText("ai.enhancement.progress.stage.extract", "抽取视频帧"),
                        update => update.ProcessedDuration is { } processed
                            ? FormatLocalizedText(
                                "ai.enhancement.progress.detail.extract.duration",
                                "已抽取到视频时间：{time}",
                                ("time", processed.ToString(@"hh\:mm\:ss\.ff", CultureInfo.InvariantCulture)))
                            : GetLocalizedText(
                                "ai.enhancement.progress.detail.extract.running",
                                "正在持续抽取视频帧…"))
                },
                cancellationToken)
            .ConfigureAwait(false);

        ThrowIfFfmpegFailed(result, AiEnhancementFailureKind.ExecutionFailed, "抽取视频帧失败。");
    }

    private async Task<EncodeExecutionResult> EncodeOutputAsync(
        string ffmpegExecutablePath,
        string inputFramesDirectory,
        string sourceInputPath,
        string outputPath,
        string outputExtension,
        bool preserveOriginalAudio,
        TimeSpan sourceDuration,
        double sourceFrameRate,
        AiEnhancementScalePlan scalePlan,
        IProgress<AiEnhancementProgress>? progress,
        CancellationToken cancellationToken)
    {
        ReportProgress(
            progress,
            AiEnhancementProgressStage.EncodingOutput,
            GetLocalizedText("ai.enhancement.progress.stage.encode", "生成输出视频"),
            scalePlan.RequiresDownscale
                ? FormatLocalizedText(
                    "ai.enhancement.progress.detail.encode.start.downscale",
                    "正在编码增强视频、回填原音轨，并从 {upscale} 回缩到 {target}…",
                    ("upscale", scalePlan.AchievedScale.ToString(CultureInfo.InvariantCulture) + "x"),
                    ("target", scalePlan.RequestedScale.ToString(CultureInfo.InvariantCulture) + "x"))
                : GetLocalizedText(
                    "ai.enhancement.progress.detail.encode.start",
                    "正在编码增强视频并回填原音轨…"),
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
                sourceFrameRate,
                scalePlan,
                progress,
                cancellationToken)
            .ConfigureAwait(false);

        if (copyAudioResult.Result.WasSuccessful)
        {
            return new EncodeExecutionResult(copyAudioResult.OutputPath, AudioWasTranscoded: false);
        }

        if (!preserveOriginalAudio)
        {
            ThrowIfFfmpegFailed(copyAudioResult.Result, AiEnhancementFailureKind.ExecutionFailed, "编码增强视频失败。");
        }

        _logger.Log(LogLevel.Warning, "增强输出在复制原音轨时失败，正在回退为 AAC 音频转码。");

        var fallbackResult = await ExecuteEncodePassAsync(
                ffmpegExecutablePath,
                inputFramesDirectory,
                sourceInputPath,
                outputPath,
                outputExtension,
                preserveOriginalAudio,
                transcodeAudio: true,
                sourceDuration,
                sourceFrameRate,
                scalePlan,
                progress,
                cancellationToken)
            .ConfigureAwait(false);

        ThrowIfFfmpegFailed(fallbackResult.Result, AiEnhancementFailureKind.ExecutionFailed, "编码增强视频失败。");
        return new EncodeExecutionResult(fallbackResult.OutputPath, AudioWasTranscoded: true);
    }

    private async Task<EncodePassResult> ExecuteEncodePassAsync(
        string ffmpegExecutablePath,
        string inputFramesDirectory,
        string sourceInputPath,
        string outputPath,
        string outputExtension,
        bool preserveOriginalAudio,
        bool transcodeAudio,
        TimeSpan sourceDuration,
        double sourceFrameRate,
        AiEnhancementScalePlan scalePlan,
        IProgress<AiEnhancementProgress>? progress,
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
            sourceFrameRate.ToString("0.###", CultureInfo.InvariantCulture),
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

        var videoFilter = BuildEncodeVideoFilter(scalePlan);
        if (!string.IsNullOrWhiteSpace(videoFilter))
        {
            arguments.Add("-vf");
            arguments.Add(videoFilter);
        }

        arguments.Add("-c:v");
        arguments.Add("libx264");
        arguments.Add("-preset");
        arguments.Add("medium");
        arguments.Add("-crf");
        arguments.Add("18");
        arguments.Add("-pix_fmt");
        arguments.Add("yuv420p");

        if (string.Equals(outputExtension, ".mp4", StringComparison.OrdinalIgnoreCase))
        {
            arguments.Add("-movflags");
            arguments.Add("+faststart");
        }

        if (preserveOriginalAudio)
        {
            arguments.Add("-c:a");
            arguments.Add(transcodeAudio ? "aac" : "copy");
            if (transcodeAudio)
            {
                arguments.Add("-b:a");
                arguments.Add("192k");
            }

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
                        GetLocalizedText("ai.enhancement.progress.stage.encode", "生成输出视频"),
                        update => update.ProcessedDuration is { } processed
                            ? FormatLocalizedText(
                                "ai.enhancement.progress.detail.encode.duration",
                                "已编码到视频时间：{time}",
                                ("time", processed.ToString(@"hh\:mm\:ss\.ff", CultureInfo.InvariantCulture)))
                            : GetLocalizedText(
                                "ai.enhancement.progress.detail.encode.running",
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
            return new EncodePassResult(temporaryOutputPath, result);
        }

        if (File.Exists(outputPath))
        {
            File.Delete(outputPath);
        }

        File.Move(temporaryOutputPath, outputPath);
        return new EncodePassResult(outputPath, result);
    }

    private static string BuildEncodeVideoFilter(AiEnhancementScalePlan scalePlan)
    {
        var filters = new List<string>(3);
        if (scalePlan.RequiresDownscale)
        {
            filters.Add(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"scale=iw*{scalePlan.RequestedScale}/{scalePlan.AchievedScale}:ih*{scalePlan.RequestedScale}/{scalePlan.AchievedScale}:flags=lanczos"));
        }

        filters.Add("pad=ceil(iw/2)*2:ceil(ih/2)*2");
        filters.Add("setsar=1");
        return string.Join(",", filters);
    }

    private async Task ExecuteRealEsrganPassAsync(
        AiRuntimeDescriptor descriptor,
        string stagedModelPath,
        string inputDirectory,
        string outputDirectory,
        string runtimeModelName,
        int nativeScale,
        ExecutionDeviceResolution executionDevice,
        int expectedOutputCount,
        int passIndex,
        int passCount,
        IProgress<AiEnhancementProgress>? progress,
        double progressStart,
        double progressEnd,
        CancellationToken cancellationToken)
    {
        var passDisplayIndex = passIndex + 1;
        var stageTitle = GetLocalizedText("ai.enhancement.progress.stage.enhance", "运行 Real-ESRGAN 增强");
        var initialDetail = FormatLocalizedText(
            "ai.enhancement.progress.detail.enhance.start",
            "正在执行第 {currentPass}/{totalPasses} 次增强（{scale}）…",
            ("currentPass", passDisplayIndex),
            ("totalPasses", passCount),
            ("scale", nativeScale.ToString(CultureInfo.InvariantCulture) + "x"));
        ReportProgress(progress, AiEnhancementProgressStage.EnhancingFrames, stageTitle, initialDetail, progressStart);

        var arguments = new List<string>
        {
            "-i",
            inputDirectory,
            "-o",
            outputDirectory,
            "-m",
            stagedModelPath,
            "-n",
            runtimeModelName,
            "-s",
            nativeScale.ToString(CultureInfo.InvariantCulture),
            "-f",
            "png"
        };

        if (executionDevice.UseCpuFallback)
        {
            arguments.Add("-g");
            arguments.Add("-1");
        }

        var result = await ExecuteRealEsrganProcessAsync(
                descriptor.ExecutablePath,
                arguments,
                outputDirectory,
                expectedOutputCount,
                nativeScale,
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
            throw new AiEnhancementWorkflowException(
                AiEnhancementFailureKind.ExecutionFailed,
                SummarizeProcessFailure(
                    result.StandardOutput,
                    result.StandardError,
                    GetLocalizedText("ai.enhancement.failure.realesrganExecution", "Real-ESRGAN 增强执行失败。")));
        }

        if (CountGeneratedFrames(outputDirectory) < expectedOutputCount)
        {
            throw new AiEnhancementWorkflowException(
                AiEnhancementFailureKind.ExecutionFailed,
                GetLocalizedText(
                    "ai.enhancement.failure.outputFramesIncomplete",
                    "增强输出帧数量异常，未生成完整的视频帧序列。"));
        }
    }

    private async Task<RealEsrganProcessExecutionResult> ExecuteRealEsrganProcessAsync(
        string executablePath,
        IReadOnlyList<string> arguments,
        string monitoredOutputDirectory,
        int expectedOutputCount,
        int nativeScale,
        string stageTitle,
        int passIndex,
        int passCount,
        IProgress<AiEnhancementProgress>? progress,
        double progressStart,
        double progressEnd,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(executablePath)
        {
            WorkingDirectory = Path.GetDirectoryName(executablePath) ?? AppContext.BaseDirectory,
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
            throw new AiEnhancementWorkflowException(
                AiEnhancementFailureKind.ExecutionFailed,
                GetLocalizedText("ai.enhancement.failure.realesrganStart", "Real-ESRGAN 进程启动失败。"));
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        var processId = process.Id;
        using var cancellationRegistration = ExternalProcessTermination.RegisterTermination(process, cancellationToken);
        using var monitorCancellationSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var monitorTask = MonitorRealEsrganOutputDirectoryAsync(
            monitoredOutputDirectory,
            expectedOutputCount,
            nativeScale,
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
                    "Real-ESRGAN 取消后在宽限时间内仍未完全退出，已继续返回结果。")
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

        return new RealEsrganProcessExecutionResult(
            process.ExitCode,
            standardOutputBuilder.ToString(),
            standardErrorBuilder.ToString());
    }

    private async Task MonitorRealEsrganOutputDirectoryAsync(
        string outputDirectory,
        int expectedOutputCount,
        int nativeScale,
        string stageTitle,
        int passIndex,
        int passCount,
        IProgress<AiEnhancementProgress>? progress,
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
            var stageRatio = expectedOutputCount > 0
                ? Math.Clamp(generatedFrameCount / (double)expectedOutputCount, 0d, 0.98d)
                : 0d;
            var overallRatio = progressStart + ((progressEnd - progressStart) * stageRatio);
            var detail = FormatLocalizedText(
                "ai.enhancement.progress.detail.enhance.running",
                "第 {currentPass}/{totalPasses} 次增强（{scale}）：已生成 {currentFrames}/{expectedFrames} 帧",
                ("currentPass", passIndex),
                ("totalPasses", passCount),
                ("scale", nativeScale.ToString(CultureInfo.InvariantCulture) + "x"),
                ("currentFrames", generatedFrameCount),
                ("expectedFrames", expectedOutputCount));
            ReportProgress(
                progress,
                AiEnhancementProgressStage.EnhancingFrames,
                stageTitle,
                detail,
                overallRatio);

            await Task.Delay(RealEsrganOutputPollInterval, cancellationToken).ConfigureAwait(false);
        }
    }

    private static string StageModelAssets(string sessionRootPath, AiRuntimeModelDescriptor model)
    {
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

    private static (double Start, double End) ResolveEnhancementProgressRange(int passIndex, int passCount)
    {
        const double enhancementStart = 0.26d;
        const double enhancementEnd = 0.82d;

        var totalRange = enhancementEnd - enhancementStart;
        var perPassRange = totalRange / Math.Max(passCount, 1);
        var start = enhancementStart + (perPassRange * passIndex);
        return (start, start + perPassRange);
    }

    private IProgress<FFmpegProgressUpdate> CreateFfmpegProgressBridge(
        IProgress<AiEnhancementProgress>? progress,
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
                    ? AiEnhancementProgressStage.EncodingOutput
                    : overallRatio < 0.3d
                        ? AiEnhancementProgressStage.ExtractingFrames
                        : AiEnhancementProgressStage.EncodingOutput,
                stageTitle,
                detailFactory(update),
                overallRatio,
                isCompleted: update.IsCompleted && progressEnd >= 0.98d);
        });
    }

    private void ThrowIfFfmpegFailed(
        FFmpegExecutionResult result,
        AiEnhancementFailureKind failureKind,
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

        throw new AiEnhancementWorkflowException(
            failureKind,
            SummarizeProcessFailure(result.StandardOutput, result.StandardError, fallbackMessage));
    }

    private static string SummarizeProcessFailure(
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

    private double ResolveSourceFrameRate(AiEnhancementMediaDetails mediaDetails, int extractedFrameCount)
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
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            _configuration.LocalDataDirectoryName,
            _configuration.RuntimeDirectoryName,
            _configuration.AiRuntimeDirectoryName,
            WorkflowSessionsDirectoryName,
            EnhancementDirectoryName,
            Guid.NewGuid().ToString("N"));

    private static string GetFullPathOrThrow(string? path, string fallback)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new AiEnhancementWorkflowException(AiEnhancementFailureKind.InvalidInput, fallback);
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
            throw new AiEnhancementWorkflowException(AiEnhancementFailureKind.InvalidInput, fallback, exception);
        }
    }

    private void ReportProgress(
        IProgress<AiEnhancementProgress>? progress,
        AiEnhancementProgressStage stage,
        string stageTitle,
        string detailText,
        double? progressRatio,
        bool isCompleted = false)
    {
        progress?.Report(
            new AiEnhancementProgress(
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

    private sealed record AiEnhancementMediaDetails(
        string InputPath,
        string InputDirectory,
        TimeSpan MediaDuration,
        double? PrimaryVideoFrameRate,
        bool HasAudioStream);

    private sealed record ExecutionDeviceResolution(
        AiEnhancementExecutionDeviceKind Kind,
        string DisplayName,
        bool UseCpuFallback);

    private sealed record EncodeExecutionResult(
        string OutputPath,
        bool AudioWasTranscoded);

    private sealed record EncodePassResult(
        string OutputPath,
        FFmpegExecutionResult Result);

    private sealed record RealEsrganProcessExecutionResult(
        int ExitCode,
        string StandardOutput,
        string StandardError);
}
