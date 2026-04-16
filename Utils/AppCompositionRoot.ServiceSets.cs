using Vidvix.Core.Interfaces;
using Vidvix.ViewModels;

namespace Vidvix.Utils;

internal sealed record AppInfrastructureServices(
    IWindowContext WindowContext,
    IWindowIconService WindowIconService,
    IDispatcherService DispatcherService,
    IFilePickerService FilePickerService,
    IUserPreferencesService UserPreferencesService,
    IFileRevealService FileRevealService);

internal sealed record AppMediaRuntimeServices(
    IFFmpegRuntimeService RuntimeService,
    IFFmpegService FFmpegService,
    IFFmpegVideoAccelerationService VideoAccelerationService,
    IMediaInfoService MediaInfoService,
    IVideoThumbnailService VideoThumbnailService,
    IVideoPreviewService VideoPreviewService);

internal sealed record AppWorkflowServices(
    IMediaImportDiscoveryService MediaImportDiscoveryService,
    IMediaProcessingWorkflowService MediaProcessingWorkflowService,
    ITrimWorkflowService TrimWorkflowService,
    IMergeMediaAnalysisService MergeMediaAnalysisService,
    IVideoJoinWorkflowService VideoJoinWorkflowService,
    IAudioJoinWorkflowService AudioJoinWorkflowService,
    IAudioVideoComposeWorkflowService AudioVideoComposeWorkflowService);
