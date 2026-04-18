// 功能：应用组合根（统一创建 ViewModel、Service 与窗口依赖关系）
// 模块：应用基础架构
// 说明：可复用，负责依赖装配，不承载具体业务规则。
using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Vidvix.Core.Interfaces;
using Vidvix.Core.Models;
using Vidvix.Services;
using Vidvix.Services.Demucs;
using Vidvix.Services.FFmpeg;
using Vidvix.Services.MediaInfo;
using Vidvix.Services.VideoPreview;
using Vidvix.ViewModels;
using Vidvix.Views;

namespace Vidvix.Utils;

public sealed class AppCompositionRoot
{
    private readonly MainViewModel _mainViewModel;
    private readonly IUserPreferencesService _userPreferencesService;
    private readonly IWindowContext _windowContext;
    private readonly IWindowIconService _windowIconService;

    public AppCompositionRoot(DispatcherQueue dispatcherQueue)
    {
        ArgumentNullException.ThrowIfNull(dispatcherQueue);

        Configuration = new ApplicationConfiguration();
        Logger = new SimpleLogger(Configuration.MirrorLogsToConsole);
        var infrastructure = CreateInfrastructureServices(dispatcherQueue);
        _windowContext = infrastructure.WindowContext;
        _windowIconService = infrastructure.WindowIconService;
        _userPreferencesService = infrastructure.UserPreferencesService;

        var mediaRuntime = CreateMediaRuntimeServices(infrastructure.WindowContext);
        var workflows = CreateWorkflowServices(mediaRuntime);
        var trimWorkspace = CreateTrimWorkspaceViewModel(infrastructure, mediaRuntime, workflows);
        var mergeWorkspace = CreateMergeWorkspaceViewModel(infrastructure, mediaRuntime, workflows);
        var splitAudioWorkspace = CreateSplitAudioWorkspaceViewModel(infrastructure, mediaRuntime, workflows);
        var terminalWorkspace = CreateTerminalWorkspaceViewModel(mediaRuntime);
        _mainViewModel = CreateMainViewModel(
            infrastructure,
            mediaRuntime,
            workflows,
            trimWorkspace,
            mergeWorkspace,
            splitAudioWorkspace,
            terminalWorkspace);
    }

    public ApplicationConfiguration Configuration { get; }

    public ILogger Logger { get; }

    public MainWindow CreateMainWindow()
    {
        var window = new MainWindow(_mainViewModel, _userPreferencesService, Logger)
        {
            Title = Configuration.ApplicationTitle
        };

        _windowContext.SetWindow(window);
        _windowIconService.ApplyIcon(window);
        return window;
    }

    public Task InitializeAsync(CancellationToken cancellationToken = default) =>
        _mainViewModel.InitializeAsync(cancellationToken);

    private AppInfrastructureServices CreateInfrastructureServices(DispatcherQueue dispatcherQueue)
    {
        var windowContext = new WindowContext();
        var dispatcherService = new DispatcherService(dispatcherQueue);
        var userPreferencesService = new UserPreferencesService(Configuration, Logger);

        return new AppInfrastructureServices(
            windowContext,
            new WindowIconService(Configuration, Logger),
            dispatcherService,
            new FilePickerService(windowContext),
            userPreferencesService,
            new FileRevealService());
    }

    private AppMediaRuntimeServices CreateMediaRuntimeServices(IWindowContext windowContext)
    {
        var packageSource = new FFmpegPackageSource(Configuration, Logger);
        var runtimeService = new FFmpegRuntimeService(Configuration, packageSource, Logger);
        var ffmpegService = new FFmpegService(Logger);
        var terminalService = new FFmpegTerminalService(Configuration, runtimeService, Logger);
        var ffmpegVideoAccelerationService = new FFmpegVideoAccelerationService(ffmpegService, Logger);
        var demucsRuntimeService = new DemucsRuntimeService(Configuration, Logger);
        var mediaInfoService = new MediaInfoService(runtimeService, Configuration, Logger);
        var videoThumbnailService = new VideoThumbnailService(runtimeService, ffmpegService, Configuration, Logger);
        var videoPreviewService = new MpvVideoPreviewService(Configuration, windowContext, Logger);

        return new AppMediaRuntimeServices(
            runtimeService,
            ffmpegService,
            terminalService,
            ffmpegVideoAccelerationService,
            demucsRuntimeService,
            mediaInfoService,
            videoThumbnailService,
            videoPreviewService);
    }

    private AppWorkflowServices CreateWorkflowServices(AppMediaRuntimeServices mediaRuntime)
    {
        var commandBuilder = new FFmpegCommandBuilder(Configuration.FFmpegExecutableFileName);
        var mediaProcessingCommandFactory = new MediaProcessingCommandFactory(Configuration, commandBuilder);
        var videoTrimCommandFactory = new VideoTrimCommandFactory(Configuration, commandBuilder);
        var audioTrimCommandFactory = new AudioTrimCommandFactory(Configuration, commandBuilder);
        var transcodingDecisionResolver = new TranscodingDecisionResolver(mediaRuntime.VideoAccelerationService);
        var mediaProcessingWorkflowService = new MediaProcessingWorkflowService(
            Configuration,
            mediaRuntime.RuntimeService,
            mediaRuntime.FFmpegService,
            mediaRuntime.VideoAccelerationService,
            mediaRuntime.MediaInfoService,
            mediaProcessingCommandFactory,
            transcodingDecisionResolver,
            Logger);
        var audioSeparationWorkflowService = new AudioSeparationWorkflowService(
            Configuration,
            mediaRuntime.RuntimeService,
            mediaRuntime.FFmpegService,
            mediaRuntime.MediaInfoService,
            mediaProcessingCommandFactory,
            commandBuilder,
            mediaRuntime.DemucsRuntimeService,
            Logger);
        var mergeMediaAnalysisService = new MergeMediaAnalysisService(
            mediaRuntime.MediaInfoService,
            Logger);
        var videoTrimWorkflowService = new VideoTrimWorkflowService(
            Configuration,
            mediaRuntime.RuntimeService,
            mediaRuntime.FFmpegService,
            mediaRuntime.VideoAccelerationService,
            mediaRuntime.MediaInfoService,
            videoTrimCommandFactory,
            transcodingDecisionResolver);
        var videoJoinWorkflowService = new VideoJoinWorkflowService(
            Configuration,
            mediaRuntime.RuntimeService,
            mediaRuntime.FFmpegService,
            transcodingDecisionResolver);
        var audioJoinWorkflowService = new AudioJoinWorkflowService(
            Configuration,
            mediaRuntime.RuntimeService,
            mediaRuntime.FFmpegService,
            transcodingDecisionResolver);
        var audioVideoComposeWorkflowService = new AudioVideoComposeWorkflowService(
            Configuration,
            mediaRuntime.RuntimeService,
            mediaRuntime.FFmpegService,
            transcodingDecisionResolver);
        var trimWorkflowService = new TrimWorkflowService(
            Configuration,
            mediaRuntime.MediaInfoService,
            mediaRuntime.RuntimeService,
            mediaRuntime.FFmpegService,
            videoTrimWorkflowService,
            audioTrimCommandFactory,
            transcodingDecisionResolver);

        return new AppWorkflowServices(
            new MediaImportDiscoveryService(),
            mediaProcessingWorkflowService,
            audioSeparationWorkflowService,
            trimWorkflowService,
            mergeMediaAnalysisService,
            videoJoinWorkflowService,
            audioJoinWorkflowService,
            audioVideoComposeWorkflowService);
    }

    private VideoTrimWorkspaceViewModel CreateTrimWorkspaceViewModel(
        AppInfrastructureServices infrastructure,
        AppMediaRuntimeServices mediaRuntime,
        AppWorkflowServices workflows)
    {
        var dependencies = new VideoTrimWorkspaceDependencies(
            Configuration,
            workflows.TrimWorkflowService,
            infrastructure.FilePickerService,
            infrastructure.UserPreferencesService,
            infrastructure.FileRevealService,
            mediaRuntime.VideoPreviewService,
            infrastructure.DispatcherService,
            Logger);

        return new VideoTrimWorkspaceViewModel(dependencies);
    }

    private MergeViewModel CreateMergeWorkspaceViewModel(
        AppInfrastructureServices infrastructure,
        AppMediaRuntimeServices mediaRuntime,
        AppWorkflowServices workflows)
    {
        var dependencies = new MergeWorkspaceDependencies(
            infrastructure.FilePickerService,
            mediaRuntime.MediaInfoService,
            infrastructure.UserPreferencesService,
            workflows.MediaImportDiscoveryService,
            workflows.MergeMediaAnalysisService,
            workflows.VideoJoinWorkflowService,
            workflows.AudioJoinWorkflowService,
            workflows.AudioVideoComposeWorkflowService,
            infrastructure.FileRevealService,
            Configuration,
            Logger);

        return new MergeViewModel(dependencies);
    }

    private TerminalWorkspaceViewModel CreateTerminalWorkspaceViewModel(AppMediaRuntimeServices mediaRuntime) =>
        new(Configuration, mediaRuntime.TerminalService);

    private SplitAudioWorkspaceViewModel CreateSplitAudioWorkspaceViewModel(
        AppInfrastructureServices infrastructure,
        AppMediaRuntimeServices mediaRuntime,
        AppWorkflowServices workflows)
    {
        var dependencies = new SplitAudioWorkspaceDependencies(
            Configuration,
            workflows.MediaImportDiscoveryService,
            mediaRuntime.MediaInfoService,
            workflows.AudioSeparationWorkflowService,
            infrastructure.FilePickerService,
            infrastructure.UserPreferencesService,
            infrastructure.FileRevealService,
            infrastructure.DispatcherService,
            Logger);

        return new SplitAudioWorkspaceViewModel(dependencies);
    }

    private MainViewModel CreateMainViewModel(
        AppInfrastructureServices infrastructure,
        AppMediaRuntimeServices mediaRuntime,
        AppWorkflowServices workflows,
        VideoTrimWorkspaceViewModel trimWorkspace,
        MergeViewModel mergeWorkspace,
        SplitAudioWorkspaceViewModel splitAudioWorkspace,
        TerminalWorkspaceViewModel terminalWorkspace)
    {
        var dependencies = new MainViewModelDependencies(
            Configuration,
            mediaRuntime.MediaInfoService,
            mediaRuntime.VideoThumbnailService,
            workflows.MediaProcessingWorkflowService,
            workflows.MediaImportDiscoveryService,
            Logger,
            infrastructure.FilePickerService,
            infrastructure.DispatcherService,
            infrastructure.UserPreferencesService,
            infrastructure.FileRevealService);

        return new MainViewModel(
            dependencies,
            trimWorkspace,
            mergeWorkspace,
            splitAudioWorkspace,
            terminalWorkspace);
    }
}
