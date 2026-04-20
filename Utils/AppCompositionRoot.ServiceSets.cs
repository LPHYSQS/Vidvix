using Vidvix.Core.Interfaces;
using Vidvix.ViewModels;

namespace Vidvix.Utils;

internal sealed record AppInfrastructureServices(
    IWindowContext WindowContext,
    IWindowIconService WindowIconService,
    IDispatcherService DispatcherService,
    IFilePickerService FilePickerService,
    ILocalizationService LocalizationService,
    IUserPreferencesService UserPreferencesService,
    IFileRevealService FileRevealService,
    IDesktopShortcutService DesktopShortcutService,
    ISystemTrayService SystemTrayService);

internal sealed record AppMediaRuntimeServices(
    IFFmpegRuntimeService RuntimeService,
    IFFmpegService FFmpegService,
    IFFmpegTerminalService TerminalService,
    IFFmpegVideoAccelerationService VideoAccelerationService,
    IDemucsRuntimeService DemucsRuntimeService,
    IMediaInfoService MediaInfoService,
    IVideoThumbnailService VideoThumbnailService,
    IVideoPreviewService TrimVideoPreviewService,
    IVideoPreviewService SplitAudioPreviewService);

internal sealed record AppWorkflowServices(
    IMediaImportDiscoveryService MediaImportDiscoveryService,
    IMediaProcessingWorkflowService MediaProcessingWorkflowService,
    IAudioSeparationWorkflowService AudioSeparationWorkflowService,
    ITrimWorkflowService TrimWorkflowService,
    IMergeMediaAnalysisService MergeMediaAnalysisService,
    IVideoJoinWorkflowService VideoJoinWorkflowService,
    IAudioJoinWorkflowService AudioJoinWorkflowService,
    IAudioVideoComposeWorkflowService AudioVideoComposeWorkflowService);
