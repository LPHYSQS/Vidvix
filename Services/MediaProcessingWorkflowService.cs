// 功能：媒体批处理工作流服务（处理运行时准备、上下文解析、预检与单文件执行）
// 模块：视频转换模块 / 音频转换模块
// 说明：可复用，仅负责业务流程与 FFmpeg 调用，不涉及 UI。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Vidvix.Core.Interfaces;
using Vidvix.Core.Models;

namespace Vidvix.Services;

public sealed class MediaProcessingWorkflowService : IMediaProcessingWorkflowService
{
    private readonly ApplicationConfiguration _configuration;
    private readonly IFFmpegRuntimeService _ffmpegRuntimeService;
    private readonly IFFmpegService _ffmpegService;
    private readonly IFFmpegVideoAccelerationService _ffmpegVideoAccelerationService;
    private readonly IMediaInfoService _mediaInfoService;
    private readonly IMediaProcessingCommandFactory _mediaProcessingCommandFactory;
    private readonly TranscodingDecisionResolver _transcodingDecisionResolver;
    private readonly ILocalizationService _localizationService;
    private readonly ILogger _logger;

    public MediaProcessingWorkflowService(
        ApplicationConfiguration configuration,
        IFFmpegRuntimeService ffmpegRuntimeService,
        IFFmpegService ffmpegService,
        IFFmpegVideoAccelerationService ffmpegVideoAccelerationService,
        IMediaInfoService mediaInfoService,
        IMediaProcessingCommandFactory mediaProcessingCommandFactory,
        TranscodingDecisionResolver transcodingDecisionResolver,
        ILocalizationService localizationService,
        ILogger logger)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _ffmpegRuntimeService = ffmpegRuntimeService ?? throw new ArgumentNullException(nameof(ffmpegRuntimeService));
        _ffmpegService = ffmpegService ?? throw new ArgumentNullException(nameof(ffmpegService));
        _ffmpegVideoAccelerationService = ffmpegVideoAccelerationService ?? throw new ArgumentNullException(nameof(ffmpegVideoAccelerationService));
        _mediaInfoService = mediaInfoService ?? throw new ArgumentNullException(nameof(mediaInfoService));
        _mediaProcessingCommandFactory = mediaProcessingCommandFactory ?? throw new ArgumentNullException(nameof(mediaProcessingCommandFactory));
        _transcodingDecisionResolver = transcodingDecisionResolver ?? throw new ArgumentNullException(nameof(transcodingDecisionResolver));
        _localizationService = localizationService ?? throw new ArgumentNullException(nameof(localizationService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<FFmpegRuntimeResolution> EnsureRuntimeReadyAsync(CancellationToken cancellationToken = default) =>
        _ffmpegRuntimeService.EnsureAvailableAsync(cancellationToken);

    public async Task<MediaProcessingContextResolutionResult> ResolveExecutionContextAsync(
        string runtimeExecutablePath,
        MediaProcessingContext executionContext,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runtimeExecutablePath);

        var messages = new List<MediaProcessingLogMessage>();

        if (executionContext.ProcessingMode == ProcessingMode.SubtitleTrackExtract)
        {
            messages.Add(new MediaProcessingLogMessage(
                LogLevel.Info,
                executionContext.OutputFormat.Extension.Equals(".mks", StringComparison.OrdinalIgnoreCase)
                    ? GetLocalizedText(
                        "mainWindow.processingContext.subtitleExtractMks",
                        "当前为字幕轨道提取：MKS 会优先保留原始字幕编码输出，不使用 GPU。")
                    : GetLocalizedText(
                        "mainWindow.processingContext.subtitleExtractText",
                        "当前为字幕轨道提取：会按所选字幕格式输出，必要时自动转换字幕编码，不使用 GPU。")));

            return new MediaProcessingContextResolutionResult(
                executionContext with { VideoAccelerationKind = VideoAccelerationKind.None },
                messages);
        }

        var decision = await _transcodingDecisionResolver
            .ResolveAsync(
                runtimeExecutablePath,
                executionContext.OutputFormat,
                executionContext.TranscodingMode,
                executionContext.IsGpuAccelerationRequested,
                containsVideo: executionContext.WorkspaceKind != ProcessingWorkspaceKind.Audio,
                cancellationToken)
            .ConfigureAwait(false);

        messages.Add(new MediaProcessingLogMessage(
            decision.MessageLevel,
            decision.Message));

        return new MediaProcessingContextResolutionResult(
            executionContext with { VideoAccelerationKind = decision.VideoAccelerationKind },
            messages);
    }

    public async Task<MediaProcessingPreflightResult> ValidatePreconditionsAsync(
        MediaProcessingContext executionContext,
        IReadOnlyList<string> inputPaths,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(inputPaths);

        if (!TryGetRequiredTrackType(executionContext, out var requiredTrackType))
        {
            return new MediaProcessingPreflightResult();
        }

        var messages = new List<MediaProcessingLogMessage>();
        var issues = new List<MediaProcessingPreflightIssue>();

        foreach (var inputPath in inputPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();

            MediaDetailsSnapshot? snapshot = null;
            if (_mediaInfoService.TryGetCachedDetails(inputPath, out var cachedSnapshot))
            {
                snapshot = cachedSnapshot;
            }
            else
            {
                var result = await _mediaInfoService.GetMediaDetailsAsync(inputPath, cancellationToken).ConfigureAwait(false);
                if (!result.IsSuccess || result.Snapshot is null)
                {
                    var reason = result.ErrorMessage ?? GetLocalizedText(
                        "mainWindow.processingContext.preflightUnableToInspectFallback",
                        "无法提前检测该文件的媒体轨道。");
                    _logger.Log(LogLevel.Warning, $"处理前预检失败，将继续尝试处理：{inputPath}，原因：{reason}");
                    messages.Add(new MediaProcessingLogMessage(
                        LogLevel.Warning,
                        FormatLocalizedText(
                            "mainWindow.processingContext.preflightUnableToInspect",
                            $"{Path.GetFileName(inputPath)} 未能完成轨道预检，将继续尝试处理。原因：{reason}",
                            ("fileName", Path.GetFileName(inputPath)),
                            ("reason", reason))));
                    continue;
                }

                snapshot = result.Snapshot;
            }

            if (HasRequiredTrack(snapshot, requiredTrackType))
            {
                if (!TryCreateSubtitleCompatibilityFailureMessage(snapshot, executionContext, out var compatibilityFailureMessage))
                {
                    continue;
                }

                issues.Add(new MediaProcessingPreflightIssue(inputPath, compatibilityFailureMessage));
                continue;
            }

            issues.Add(new MediaProcessingPreflightIssue(
                inputPath,
                CreateMissingRequiredTrackMessage(requiredTrackType, executionContext)));
        }

        return new MediaProcessingPreflightResult(messages, issues);
    }

    public async Task<TimeSpan?> GetMediaDurationAsync(string inputPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputPath);

        if (_mediaInfoService.TryGetCachedDetails(inputPath, out var cachedSnapshot))
        {
            return cachedSnapshot.MediaDuration;
        }

        var result = await _mediaInfoService.GetMediaDetailsAsync(inputPath, cancellationToken).ConfigureAwait(false);
        return result.Snapshot?.MediaDuration;
    }

    public async Task<MediaProcessingItemExecutionResult> ExecuteAsync(
        MediaProcessingItemExecutionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var inputSnapshot = await LoadInputSnapshotAsync(request.InputPath, cancellationToken).ConfigureAwait(false);
        var command = CreateCommand(
            request.RuntimeExecutablePath,
            request.InputPath,
            request.OutputPath,
            request.ExecutionContext,
            inputSnapshot);

        var options = new FFmpegExecutionOptions
        {
            Timeout = _configuration.DefaultExecutionTimeout,
            InputDuration = request.InputDuration,
            Progress = request.Progress
        };

        var totalDuration = TimeSpan.Zero;
        var result = await _ffmpegService.ExecuteAsync(command, options, cancellationToken).ConfigureAwait(false);
        totalDuration += result.Duration;

        var usedCpuFallback = false;
        if (ShouldRetryWithCpuFallback(request.ExecutionContext, result))
        {
            usedCpuFallback = true;
            request.OnCpuFallback?.Invoke();

            var cpuFallbackContext = request.ExecutionContext with
            {
                VideoAccelerationKind = VideoAccelerationKind.None
            };

            command = CreateCommand(
                request.RuntimeExecutablePath,
                request.InputPath,
                request.OutputPath,
                cpuFallbackContext,
                inputSnapshot);

            result = await _ffmpegService.ExecuteAsync(command, options, cancellationToken).ConfigureAwait(false);
            totalDuration += result.Duration;
        }

        return new MediaProcessingItemExecutionResult(result, usedCpuFallback, totalDuration);
    }

    private FFmpegCommand CreateCommand(
        string runtimeExecutablePath,
        string inputPath,
        string outputPath,
        MediaProcessingContext executionContext,
        MediaDetailsSnapshot? inputSnapshot) =>
        _mediaProcessingCommandFactory.Create(
            new MediaProcessingCommandRequest(
                runtimeExecutablePath,
                inputPath,
                outputPath,
                executionContext,
                inputSnapshot));

    private async Task<MediaDetailsSnapshot?> LoadInputSnapshotAsync(string inputPath, CancellationToken cancellationToken)
    {
        if (_mediaInfoService.TryGetCachedDetails(inputPath, out var cachedSnapshot))
        {
            return cachedSnapshot;
        }

        var loadResult = await _mediaInfoService.GetMediaDetailsAsync(inputPath, cancellationToken).ConfigureAwait(false);
        return loadResult.IsSuccess ? loadResult.Snapshot : null;
    }

    private static bool TryGetRequiredTrackType(
        MediaProcessingContext executionContext,
        out RequiredTrackType requiredTrackType)
    {
        if (executionContext.WorkspaceKind == ProcessingWorkspaceKind.Audio)
        {
            requiredTrackType = RequiredTrackType.Audio;
            return true;
        }

        switch (executionContext.ProcessingMode)
        {
            case ProcessingMode.VideoTrackExtract:
                requiredTrackType = RequiredTrackType.Video;
                return true;
            case ProcessingMode.AudioTrackExtract:
                requiredTrackType = RequiredTrackType.Audio;
                return true;
            case ProcessingMode.SubtitleTrackExtract:
                requiredTrackType = RequiredTrackType.Subtitle;
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
            RequiredTrackType.Subtitle => snapshot.HasSubtitleStream,
            _ => false
        };

    private bool TryCreateSubtitleCompatibilityFailureMessage(
        MediaDetailsSnapshot snapshot,
        MediaProcessingContext executionContext,
        out string failureMessage)
    {
        failureMessage = string.Empty;

        if (executionContext.WorkspaceKind != ProcessingWorkspaceKind.Video ||
            executionContext.ProcessingMode != ProcessingMode.SubtitleTrackExtract ||
            executionContext.OutputFormat.Extension.Equals(".mks", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!IsBitmapSubtitleCodec(snapshot.PrimarySubtitleCodecName))
        {
            return false;
        }

        failureMessage = FormatLocalizedText(
            "mainWindow.processingContext.graphicalSubtitleBlocked",
            $"检测到图形字幕轨道，无法直接转换为 {executionContext.OutputFormat.DisplayName} 文本字幕；请改用 MKS 保留原字幕轨道。",
            ("format", executionContext.OutputFormat.DisplayName));
        return true;
    }

    private static bool IsBitmapSubtitleCodec(string? codecName) =>
        !string.IsNullOrWhiteSpace(codecName) &&
        codecName.ToLowerInvariant() is "hdmv_pgs_subtitle" or "dvd_subtitle" or "dvb_subtitle" or "xsub";

    private string CreateMissingRequiredTrackMessage(
        RequiredTrackType requiredTrackType,
        MediaProcessingContext executionContext)
    {
        var outputFormatName = executionContext.OutputFormat.DisplayName;

        if (executionContext.WorkspaceKind == ProcessingWorkspaceKind.Audio)
        {
            return FormatLocalizedText(
                "mainWindow.processingContext.missingAudioForOutput",
                $"未检测到音频流，无法转换为 {outputFormatName}。",
                ("format", outputFormatName));
        }

        return requiredTrackType switch
        {
            RequiredTrackType.Video => FormatLocalizedText(
                "mainWindow.processingContext.missingVideoTrackForOutput",
                $"未检测到视频轨道，无法提取为 {outputFormatName}。",
                ("format", outputFormatName)),
            RequiredTrackType.Audio => FormatLocalizedText(
                "mainWindow.processingContext.missingAudioTrackForOutput",
                $"未检测到音频轨道，无法提取为 {outputFormatName}。",
                ("format", outputFormatName)),
            RequiredTrackType.Subtitle => FormatLocalizedText(
                "mainWindow.processingContext.missingSubtitleTrackForOutput",
                $"未检测到字幕轨道，无法提取为 {outputFormatName}。",
                ("format", outputFormatName)),
            _ => GetLocalizedText("mainWindow.processingContext.modeMismatch", "当前文件不满足所选处理模式。")
        };
    }

    private string GetLocalizedText(string key, string fallback) =>
        _localizationService.GetString(key, fallback);

    private string FormatLocalizedText(string key, string fallback, params (string Name, object? Value)[] arguments)
    {
        Dictionary<string, object?>? localizedArguments = null;
        if (arguments.Length > 0)
        {
            localizedArguments = new Dictionary<string, object?>(arguments.Length, StringComparer.Ordinal);
            foreach (var argument in arguments)
            {
                localizedArguments[argument.Name] = argument.Value;
            }
        }

        return _localizationService.Format(key, localizedArguments, fallback);
    }

    private static bool ShouldRetryWithCpuFallback(MediaProcessingContext executionContext, FFmpegExecutionResult result) =>
        executionContext.VideoAccelerationKind != VideoAccelerationKind.None &&
        !result.WasSuccessful &&
        !result.WasCancelled &&
        !result.TimedOut;

    private enum RequiredTrackType
    {
        Video,
        Audio,
        Subtitle
    }
}
