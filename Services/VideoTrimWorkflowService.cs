// 功能：视频裁剪工作流服务（处理导入校验、媒体信息解析与裁剪导出执行）
// 模块：裁剪模块
// 说明：可复用，仅负责裁剪业务逻辑，不涉及 UI。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Vidvix.Core.Interfaces;
using Vidvix.Core.Models;
using Vidvix.Services.FFmpeg;

namespace Vidvix.Services;

public sealed class VideoTrimWorkflowService : IVideoTrimWorkflowService
{
    private readonly ApplicationConfiguration _configuration;
    private readonly IFFmpegRuntimeService _ffmpegRuntimeService;
    private readonly IFFmpegService _ffmpegService;
    private readonly IFFmpegVideoAccelerationService _ffmpegVideoAccelerationService;
    private readonly IMediaInfoService _mediaInfoService;
    private readonly IVideoTrimCommandFactory _videoTrimCommandFactory;

    public VideoTrimWorkflowService(
        ApplicationConfiguration configuration,
        IFFmpegRuntimeService ffmpegRuntimeService,
        IFFmpegService ffmpegService,
        IFFmpegVideoAccelerationService ffmpegVideoAccelerationService,
        IMediaInfoService mediaInfoService,
        IVideoTrimCommandFactory videoTrimCommandFactory)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _ffmpegRuntimeService = ffmpegRuntimeService ?? throw new ArgumentNullException(nameof(ffmpegRuntimeService));
        _ffmpegService = ffmpegService ?? throw new ArgumentNullException(nameof(ffmpegService));
        _ffmpegVideoAccelerationService = ffmpegVideoAccelerationService ?? throw new ArgumentNullException(nameof(ffmpegVideoAccelerationService));
        _mediaInfoService = mediaInfoService ?? throw new ArgumentNullException(nameof(mediaInfoService));
        _videoTrimCommandFactory = videoTrimCommandFactory ?? throw new ArgumentNullException(nameof(videoTrimCommandFactory));
    }

    public async Task<VideoTrimImportResult> ImportAsync(
        IEnumerable<string> inputPaths,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(inputPaths);

        var paths = inputPaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (paths.Length == 0)
        {
            return VideoTrimImportResult.Rejected(string.Empty);
        }

        if (paths.Length != 1)
        {
            return VideoTrimImportResult.Rejected("裁剪模块一次只能导入 1 个视频文件。");
        }

        var inputPath = paths[0];
        if (Directory.Exists(inputPath))
        {
            return VideoTrimImportResult.Rejected("裁剪模块仅支持导入单个视频文件，不支持文件夹。");
        }

        if (!File.Exists(inputPath) ||
            !_configuration.SupportedTrimInputFileTypes.Contains(Path.GetExtension(inputPath), StringComparer.OrdinalIgnoreCase))
        {
            return VideoTrimImportResult.Rejected("当前文件类型不在裁剪模块支持范围内。");
        }

        var details = await _mediaInfoService.GetMediaDetailsAsync(inputPath, cancellationToken).ConfigureAwait(false);
        var duration = details.Snapshot?.MediaDuration;
        if (!details.IsSuccess ||
            details.Snapshot is null ||
            !details.Snapshot.HasVideoStream ||
            duration is null ||
            duration <= TimeSpan.Zero)
        {
            return VideoTrimImportResult.Failed(
                ResolveImportFailureMessage(details),
                details.DiagnosticDetails);
        }

        var inputFileName = Path.GetFileName(inputPath);
        return VideoTrimImportResult.Success(
            inputPath,
            inputFileName,
            details.Snapshot,
            duration.Value,
            $"已导入 {inputFileName}，请拖动入点和出点确认裁剪范围。");
    }

    public async Task<VideoTrimExportResult> ExportAsync(
        VideoTrimExportRequest request,
        UserPreferences preferences,
        IProgress<FFmpegProgressUpdate>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(preferences);

        var runtime = await _ffmpegRuntimeService.EnsureAvailableAsync(cancellationToken).ConfigureAwait(false);
        var videoAccelerationKind = await ResolveVideoAccelerationKindAsync(
            runtime.ExecutablePath,
            request.OutputFormat,
            preferences,
            cancellationToken).ConfigureAwait(false);

        var resolvedRequest = request with
        {
            VideoAccelerationKind = videoAccelerationKind
        };

        var command = _videoTrimCommandFactory.Create(resolvedRequest, runtime.ExecutablePath);
        var options = new FFmpegExecutionOptions
        {
            Timeout = _configuration.DefaultExecutionTimeout,
            InputDuration = resolvedRequest.Duration,
            Progress = progress
        };

        var executionResult = await _ffmpegService
            .ExecuteAsync(command, options, cancellationToken)
            .ConfigureAwait(false);

        return new VideoTrimExportResult(resolvedRequest, executionResult);
    }

    private async Task<VideoAccelerationKind> ResolveVideoAccelerationKindAsync(
        string runtimeExecutablePath,
        OutputFormatOption outputFormat,
        UserPreferences preferences,
        CancellationToken cancellationToken)
    {
        if (preferences.PreferredTranscodingMode != TranscodingMode.FullTranscode ||
            !preferences.EnableGpuAccelerationForTranscoding ||
            !FFmpegVideoEncodingPolicy.SupportsHardwareVideoEncoding(outputFormat))
        {
            return VideoAccelerationKind.None;
        }

        var probeResult = await _ffmpegVideoAccelerationService
            .ProbeBestEncoderAsync(runtimeExecutablePath, cancellationToken)
            .ConfigureAwait(false);

        return probeResult.IsAvailable ? probeResult.Kind : VideoAccelerationKind.None;
    }

    private static string ResolveImportFailureMessage(MediaDetailsLoadResult result)
    {
        if (!string.IsNullOrWhiteSpace(result.ErrorMessage))
        {
            return result.ErrorMessage;
        }

        if (result.Snapshot is null)
        {
            return "无法解析当前视频文件。";
        }

        if (!result.Snapshot.HasVideoStream)
        {
            return "当前文件不包含可裁剪的视频流。";
        }

        return "当前视频时长无效，无法开始裁剪。";
    }
}
