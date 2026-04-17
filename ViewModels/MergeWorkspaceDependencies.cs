using Vidvix.Core.Interfaces;
using Vidvix.Core.Models;

namespace Vidvix.ViewModels;

internal sealed class MergeWorkspaceDependencies
{
    public MergeWorkspaceDependencies(
        IFilePickerService? filePickerService = null,
        IMediaInfoService? mediaInfoService = null,
        IUserPreferencesService? userPreferencesService = null,
        IMergeMediaAnalysisService? mergeMediaAnalysisService = null,
        IVideoJoinWorkflowService? videoJoinWorkflowService = null,
        IAudioJoinWorkflowService? audioJoinWorkflowService = null,
        IAudioVideoComposeWorkflowService? audioVideoComposeWorkflowService = null,
        IFileRevealService? fileRevealService = null,
        ApplicationConfiguration? configuration = null,
        ILogger? logger = null)
    {
        FilePickerService = filePickerService;
        MediaInfoService = mediaInfoService;
        UserPreferencesService = userPreferencesService;
        MergeMediaAnalysisService = mergeMediaAnalysisService;
        VideoJoinWorkflowService = videoJoinWorkflowService;
        AudioJoinWorkflowService = audioJoinWorkflowService;
        AudioVideoComposeWorkflowService = audioVideoComposeWorkflowService;
        FileRevealService = fileRevealService;
        Configuration = configuration;
        Logger = logger;
    }

    public IFilePickerService? FilePickerService { get; }

    public IMediaInfoService? MediaInfoService { get; }

    public IUserPreferencesService? UserPreferencesService { get; }

    public IMergeMediaAnalysisService? MergeMediaAnalysisService { get; }

    public IVideoJoinWorkflowService? VideoJoinWorkflowService { get; }

    public IAudioJoinWorkflowService? AudioJoinWorkflowService { get; }

    public IAudioVideoComposeWorkflowService? AudioVideoComposeWorkflowService { get; }

    public IFileRevealService? FileRevealService { get; }

    public ApplicationConfiguration? Configuration { get; }

    public ILogger? Logger { get; }
}
