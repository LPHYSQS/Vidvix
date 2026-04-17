using System;
using Vidvix.Core.Interfaces;
using Vidvix.Core.Models;

namespace Vidvix.ViewModels;

internal sealed class MainViewModelDependencies
{
    public MainViewModelDependencies(
        ApplicationConfiguration configuration,
        IMediaInfoService mediaInfoService,
        IVideoThumbnailService videoThumbnailService,
        IMediaProcessingWorkflowService mediaProcessingWorkflowService,
        IMediaImportDiscoveryService mediaImportDiscoveryService,
        ILogger logger,
        IFilePickerService filePickerService,
        IDispatcherService dispatcherService,
        IUserPreferencesService userPreferencesService,
        IFileRevealService fileRevealService)
    {
        Configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        MediaInfoService = mediaInfoService ?? throw new ArgumentNullException(nameof(mediaInfoService));
        VideoThumbnailService = videoThumbnailService ?? throw new ArgumentNullException(nameof(videoThumbnailService));
        MediaProcessingWorkflowService = mediaProcessingWorkflowService ?? throw new ArgumentNullException(nameof(mediaProcessingWorkflowService));
        MediaImportDiscoveryService = mediaImportDiscoveryService ?? throw new ArgumentNullException(nameof(mediaImportDiscoveryService));
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
        FilePickerService = filePickerService ?? throw new ArgumentNullException(nameof(filePickerService));
        DispatcherService = dispatcherService ?? throw new ArgumentNullException(nameof(dispatcherService));
        UserPreferencesService = userPreferencesService ?? throw new ArgumentNullException(nameof(userPreferencesService));
        FileRevealService = fileRevealService ?? throw new ArgumentNullException(nameof(fileRevealService));
    }

    public ApplicationConfiguration Configuration { get; }

    public IMediaInfoService MediaInfoService { get; }

    public IVideoThumbnailService VideoThumbnailService { get; }

    public IMediaProcessingWorkflowService MediaProcessingWorkflowService { get; }

    public IMediaImportDiscoveryService MediaImportDiscoveryService { get; }

    public ILogger Logger { get; }

    public IFilePickerService FilePickerService { get; }

    public IDispatcherService DispatcherService { get; }

    public IUserPreferencesService UserPreferencesService { get; }

    public IFileRevealService FileRevealService { get; }
}
