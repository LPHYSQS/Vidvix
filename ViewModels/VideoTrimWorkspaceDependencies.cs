using System;
using Vidvix.Core.Interfaces;
using Vidvix.Core.Models;

namespace Vidvix.ViewModels;

internal sealed class VideoTrimWorkspaceDependencies
{
    public VideoTrimWorkspaceDependencies(
        ApplicationConfiguration configuration,
        ITrimWorkflowService trimWorkflowService,
        IFilePickerService filePickerService,
        IUserPreferencesService userPreferencesService,
        IFileRevealService fileRevealService,
        IVideoPreviewService videoPreviewService,
        IDispatcherService dispatcherService,
        ILogger logger)
    {
        Configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        TrimWorkflowService = trimWorkflowService ?? throw new ArgumentNullException(nameof(trimWorkflowService));
        FilePickerService = filePickerService ?? throw new ArgumentNullException(nameof(filePickerService));
        UserPreferencesService = userPreferencesService ?? throw new ArgumentNullException(nameof(userPreferencesService));
        FileRevealService = fileRevealService ?? throw new ArgumentNullException(nameof(fileRevealService));
        VideoPreviewService = videoPreviewService ?? throw new ArgumentNullException(nameof(videoPreviewService));
        DispatcherService = dispatcherService ?? throw new ArgumentNullException(nameof(dispatcherService));
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public ApplicationConfiguration Configuration { get; }

    public ITrimWorkflowService TrimWorkflowService { get; }

    public IFilePickerService FilePickerService { get; }

    public IUserPreferencesService UserPreferencesService { get; }

    public IFileRevealService FileRevealService { get; }

    public IVideoPreviewService VideoPreviewService { get; }

    public IDispatcherService DispatcherService { get; }

    public ILogger Logger { get; }
}
