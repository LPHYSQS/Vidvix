using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Vidvix.Core.Interfaces;
using Vidvix.Core.Models;
using Vidvix.Services;
using Vidvix.Services.FFmpeg;
using Vidvix.Services.MediaInfo;
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

        _windowContext = new WindowContext();
        _windowIconService = new WindowIconService(Configuration, Logger);
        var dispatcherService = new DispatcherService(dispatcherQueue);
        var filePickerService = new FilePickerService(_windowContext);
        var mediaImportDiscoveryService = new MediaImportDiscoveryService();
        var packageSource = new FFmpegPackageSource(Configuration, Logger);
        var runtimeService = new FFmpegRuntimeService(Configuration, packageSource, Logger);
        var ffmpegService = new FFmpegService(Logger);
        var ffmpegVideoAccelerationService = new FFmpegVideoAccelerationService(ffmpegService, Logger);
        var mediaInfoService = new MediaInfoService(runtimeService, Configuration, Logger);
        var videoThumbnailService = new VideoThumbnailService(runtimeService, ffmpegService, Configuration, Logger);
        var commandBuilder = new FFmpegCommandBuilder(Configuration.FFmpegExecutableFileName);
        _userPreferencesService = new UserPreferencesService(Configuration, Logger);
        var fileRevealService = new FileRevealService();

        _mainViewModel = new MainViewModel(
            Configuration,
            runtimeService,
            ffmpegService,
            ffmpegVideoAccelerationService,
            mediaInfoService,
            videoThumbnailService,
            commandBuilder,
            mediaImportDiscoveryService,
            Logger,
            filePickerService,
            dispatcherService,
            _userPreferencesService,
            fileRevealService);
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
}
