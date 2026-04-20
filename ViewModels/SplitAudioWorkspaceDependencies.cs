using System;
using Vidvix.Core.Interfaces;
using Vidvix.Core.Models;

namespace Vidvix.ViewModels;

internal sealed class SplitAudioWorkspaceDependencies
{
    public SplitAudioWorkspaceDependencies(
        ApplicationConfiguration configuration,
        ILocalizationService localizationService,
        IMediaImportDiscoveryService mediaImportDiscoveryService,
        IMediaInfoService mediaInfoService,
        IAudioSeparationWorkflowService audioSeparationWorkflowService,
        IFilePickerService filePickerService,
        IUserPreferencesService userPreferencesService,
        IFileRevealService fileRevealService,
        IVideoPreviewService videoPreviewService,
        IDispatcherService dispatcherService,
        ILogger logger)
    {
        Configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        LocalizationService = localizationService ?? throw new ArgumentNullException(nameof(localizationService));
        MediaImportDiscoveryService = mediaImportDiscoveryService ?? throw new ArgumentNullException(nameof(mediaImportDiscoveryService));
        MediaInfoService = mediaInfoService ?? throw new ArgumentNullException(nameof(mediaInfoService));
        AudioSeparationWorkflowService = audioSeparationWorkflowService ?? throw new ArgumentNullException(nameof(audioSeparationWorkflowService));
        FilePickerService = filePickerService ?? throw new ArgumentNullException(nameof(filePickerService));
        UserPreferencesService = userPreferencesService ?? throw new ArgumentNullException(nameof(userPreferencesService));
        FileRevealService = fileRevealService ?? throw new ArgumentNullException(nameof(fileRevealService));
        VideoPreviewService = videoPreviewService ?? throw new ArgumentNullException(nameof(videoPreviewService));
        DispatcherService = dispatcherService ?? throw new ArgumentNullException(nameof(dispatcherService));
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public ApplicationConfiguration Configuration { get; }

    public ILocalizationService LocalizationService { get; }

    public IMediaImportDiscoveryService MediaImportDiscoveryService { get; }

    public IMediaInfoService MediaInfoService { get; }

    public IAudioSeparationWorkflowService AudioSeparationWorkflowService { get; }

    public IFilePickerService FilePickerService { get; }

    public IUserPreferencesService UserPreferencesService { get; }

    public IFileRevealService FileRevealService { get; }

    public IVideoPreviewService VideoPreviewService { get; }

    public IDispatcherService DispatcherService { get; }

    public ILogger Logger { get; }
}
