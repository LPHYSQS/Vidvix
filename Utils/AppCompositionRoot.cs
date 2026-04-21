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
    private readonly ILocalizationService _localizationService;
    private readonly IUserPreferencesService _userPreferencesService;
    private readonly IWindowContext _windowContext;
    private readonly IWindowIconService _windowIconService;
    private readonly ISystemTrayService _systemTrayService;

    public AppCompositionRoot(DispatcherQueue dispatcherQueue)
    {
        ArgumentNullException.ThrowIfNull(dispatcherQueue);

        Configuration = new ApplicationConfiguration();
        Logger = new SimpleLogger(Configuration.MirrorLogsToConsole);
        var infrastructure = CreateInfrastructureServices(dispatcherQueue);
        _windowContext = infrastructure.WindowContext;
        _windowIconService = infrastructure.WindowIconService;
        _localizationService = infrastructure.LocalizationService;
        _userPreferencesService = infrastructure.UserPreferencesService;
        _systemTrayService = infrastructure.SystemTrayService;

        var mediaRuntime = CreateMediaRuntimeServices(infrastructure.WindowContext, infrastructure.LocalizationService);
        var workflows = CreateWorkflowServices(mediaRuntime, infrastructure.LocalizationService);
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

    public ILocalizationService LocalizationService => _localizationService;

    public MainWindow CreateMainWindow()
    {
        var window = new MainWindow(_mainViewModel, _userPreferencesService, _systemTrayService, Logger)
        {
            Title = Configuration.ApplicationTitle
        };

        _windowContext.SetWindow(window);
        _windowIconService.ApplyIcon(window);
        return window;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await _localizationService.InitializeAsync(cancellationToken);
        await _mainViewModel.InitializeAsync(cancellationToken);
    }

    private AppInfrastructureServices CreateInfrastructureServices(DispatcherQueue dispatcherQueue)
    {
        var windowContext = new WindowContext();
        var dispatcherService = new DispatcherService(dispatcherQueue);
        var userPreferencesService = new UserPreferencesService(Configuration, Logger);
        var localizationService = new LocalizationService(Configuration, userPreferencesService, Logger);

        return new AppInfrastructureServices(
            windowContext,
            new WindowIconService(Configuration, Logger),
            dispatcherService,
            new FilePickerService(windowContext),
            localizationService,
            userPreferencesService,
            new FileRevealService(),
            new DesktopShortcutService(Configuration, Logger),
            new SystemTrayService(Configuration, dispatcherService, Logger));
    }

    private AppMediaRuntimeServices CreateMediaRuntimeServices(
        IWindowContext windowContext,
        ILocalizationService localizationService)
    {
        var packageSource = new FFmpegPackageSource(Configuration, Logger);
        var runtimeService = new FFmpegRuntimeService(Configuration, packageSource, Logger);
        var ffmpegService = new FFmpegService(Logger);
        var terminalService = new FFmpegTerminalService(Configuration, runtimeService, localizationService, Logger);
        var ffmpegVideoAccelerationService = new FFmpegVideoAccelerationService(ffmpegService, localizationService, Logger);
        var demucsRuntimeService = new DemucsRuntimeService(Configuration, localizationService, Logger);
        var mediaInfoService = new MediaInfoService(runtimeService, Configuration, localizationService, Logger);
        var videoThumbnailService = new VideoThumbnailService(runtimeService, ffmpegService, Configuration, Logger);
        var trimVideoPreviewService = new MpvVideoPreviewService(Configuration, windowContext, Logger);
        var splitAudioPreviewService = new MpvVideoPreviewService(Configuration, windowContext, Logger);

        return new AppMediaRuntimeServices(
            runtimeService,
            ffmpegService,
            terminalService,
            ffmpegVideoAccelerationService,
            demucsRuntimeService,
            mediaInfoService,
            videoThumbnailService,
            trimVideoPreviewService,
            splitAudioPreviewService);
    }

    private AppWorkflowServices CreateWorkflowServices(
        AppMediaRuntimeServices mediaRuntime,
        ILocalizationService localizationService)
    {
        var commandBuilder = new FFmpegCommandBuilder(Configuration.FFmpegExecutableFileName);
        var mediaProcessingCommandFactory = new MediaProcessingCommandFactory(Configuration, commandBuilder);
        var videoTrimCommandFactory = new VideoTrimCommandFactory(Configuration, commandBuilder);
        var audioTrimCommandFactory = new AudioTrimCommandFactory(Configuration, commandBuilder);
        var transcodingDecisionResolver = new TranscodingDecisionResolver(mediaRuntime.VideoAccelerationService, localizationService);
        var demucsExecutionPlanner = new DemucsExecutionPlanner(
            Configuration,
            mediaRuntime.DemucsRuntimeService,
            localizationService,
            Logger);
        var mediaProcessingWorkflowService = new MediaProcessingWorkflowService(
            Configuration,
            mediaRuntime.RuntimeService,
            mediaRuntime.FFmpegService,
            mediaRuntime.VideoAccelerationService,
            mediaRuntime.MediaInfoService,
            mediaProcessingCommandFactory,
            transcodingDecisionResolver,
            localizationService,
            Logger);
        var audioSeparationWorkflowService = new AudioSeparationWorkflowService(
            Configuration,
            mediaRuntime.RuntimeService,
            mediaRuntime.FFmpegService,
            mediaRuntime.MediaInfoService,
            mediaProcessingCommandFactory,
            commandBuilder,
            demucsExecutionPlanner,
            localizationService,
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
            transcodingDecisionResolver,
            localizationService);
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
            transcodingDecisionResolver,
            localizationService);

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
            infrastructure.LocalizationService,
            workflows.TrimWorkflowService,
            infrastructure.FilePickerService,
            infrastructure.UserPreferencesService,
            infrastructure.FileRevealService,
            mediaRuntime.TrimVideoPreviewService,
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
            infrastructure.LocalizationService,
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
        new(Configuration, _localizationService, mediaRuntime.TerminalService);

    private SplitAudioWorkspaceViewModel CreateSplitAudioWorkspaceViewModel(
        AppInfrastructureServices infrastructure,
        AppMediaRuntimeServices mediaRuntime,
        AppWorkflowServices workflows)
    {
        var dependencies = new SplitAudioWorkspaceDependencies(
            Configuration,
            infrastructure.LocalizationService,
            workflows.MediaImportDiscoveryService,
            mediaRuntime.MediaInfoService,
            workflows.AudioSeparationWorkflowService,
            infrastructure.FilePickerService,
            infrastructure.UserPreferencesService,
            infrastructure.FileRevealService,
            mediaRuntime.SplitAudioPreviewService,
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
            infrastructure.LocalizationService,
            Logger,
            infrastructure.FilePickerService,
            infrastructure.DispatcherService,
            infrastructure.UserPreferencesService,
            infrastructure.FileRevealService,
            infrastructure.DesktopShortcutService);

        return new MainViewModel(
            dependencies,
            trimWorkspace,
            mergeWorkspace,
            splitAudioWorkspace,
            terminalWorkspace);
    }
}
