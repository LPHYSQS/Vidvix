using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Vidvix.Core.Interfaces;
using Vidvix.Core.Models;

namespace Vidvix.Services;

public sealed class TrimWorkflowService : ITrimWorkflowService
{
    private readonly ApplicationConfiguration _configuration;
    private readonly IMediaInfoService _mediaInfoService;
    private readonly IFFmpegRuntimeService _ffmpegRuntimeService;
    private readonly IFFmpegService _ffmpegService;
    private readonly IVideoTrimWorkflowService _videoTrimWorkflowService;
    private readonly IAudioTrimCommandFactory _audioTrimCommandFactory;

    public TrimWorkflowService(
        ApplicationConfiguration configuration,
        IMediaInfoService mediaInfoService,
        IFFmpegRuntimeService ffmpegRuntimeService,
        IFFmpegService ffmpegService,
        IVideoTrimWorkflowService videoTrimWorkflowService,
        IAudioTrimCommandFactory audioTrimCommandFactory)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _mediaInfoService = mediaInfoService ?? throw new ArgumentNullException(nameof(mediaInfoService));
        _ffmpegRuntimeService = ffmpegRuntimeService ?? throw new ArgumentNullException(nameof(ffmpegRuntimeService));
        _ffmpegService = ffmpegService ?? throw new ArgumentNullException(nameof(ffmpegService));
        _videoTrimWorkflowService = videoTrimWorkflowService ?? throw new ArgumentNullException(nameof(videoTrimWorkflowService));
        _audioTrimCommandFactory = audioTrimCommandFactory ?? throw new ArgumentNullException(nameof(audioTrimCommandFactory));
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
            return VideoTrimImportResult.Rejected("裁剪模块一次只能导入 1 个文件。");
        }

        var inputPath = paths[0];
        if (Directory.Exists(inputPath))
        {
            return VideoTrimImportResult.Rejected("裁剪模块仅支持导入单个音频或视频文件，不支持文件夹。");
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
            duration is null ||
            duration <= TimeSpan.Zero)
        {
            return VideoTrimImportResult.Failed(
                ResolveImportFailureMessage(details),
                details.DiagnosticDetails);
        }

        if (!TryResolveMediaKind(details.Snapshot, out var mediaKind))
        {
            return VideoTrimImportResult.Failed(
                ResolveImportFailureMessage(details),
                details.DiagnosticDetails);
        }

        var inputFileName = Path.GetFileName(inputPath);
        return VideoTrimImportResult.Success(
            inputPath,
            inputFileName,
            mediaKind,
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

        if (request.MediaKind == TrimMediaKind.Video)
        {
            return await _videoTrimWorkflowService
                .ExportAsync(request, preferences, progress, cancellationToken)
                .ConfigureAwait(false);
        }

        var runtime = await _ffmpegRuntimeService.EnsureAvailableAsync(cancellationToken).ConfigureAwait(false);
        var command = _audioTrimCommandFactory.Create(request, runtime.ExecutablePath);
        var options = new FFmpegExecutionOptions
        {
            Timeout = _configuration.DefaultExecutionTimeout,
            InputDuration = request.Duration,
            Progress = progress
        };

        var executionResult = await _ffmpegService
            .ExecuteAsync(command, options, cancellationToken)
            .ConfigureAwait(false);

        return new VideoTrimExportResult(request, executionResult);
    }

    private static bool TryResolveMediaKind(MediaDetailsSnapshot snapshot, out TrimMediaKind mediaKind)
    {
        if (snapshot.HasVideoStream)
        {
            mediaKind = TrimMediaKind.Video;
            return true;
        }

        if (snapshot.HasAudioStream)
        {
            mediaKind = TrimMediaKind.Audio;
            return true;
        }

        mediaKind = default;
        return false;
    }

    private static string ResolveImportFailureMessage(MediaDetailsLoadResult result)
    {
        if (!string.IsNullOrWhiteSpace(result.ErrorMessage))
        {
            return result.ErrorMessage;
        }

        if (result.Snapshot is null)
        {
            return "无法解析当前文件。";
        }

        if (!result.Snapshot.HasVideoStream && !result.Snapshot.HasAudioStream)
        {
            return "当前文件不包含可裁剪的音频或视频流。";
        }

        return "当前文件时长无效，无法开始裁剪。";
    }
}
