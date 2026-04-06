using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Vidvix.Core.Interfaces;
using Vidvix.Core.Models;
using Vidvix.Services;
using Vidvix.Services.FFmpeg;
using Vidvix.ViewModels;
using Vidvix.Views;

namespace Vidvix.Utils;

public sealed class AppCompositionRoot
{
    private readonly MainViewModel _mainViewModel;
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
        var mediaImportDiscoveryService = new MediaImportDiscoveryService(Configuration);
        var packageSource = new FFmpegPackageSource(Configuration, Logger);
        var runtimeService = new FFmpegRuntimeService(Configuration, packageSource, Logger);
        var ffmpegService = new FFmpegService(Logger);
        var commandBuilder = new FFmpegCommandBuilder(Configuration.FFmpegExecutableFileName);
        var userPreferencesService = new UserPreferencesService(Configuration, Logger);
        var fileRevealService = new FileRevealService();

        _mainViewModel = new MainViewModel(
            Configuration,
            runtimeService,
            ffmpegService,
            commandBuilder,
            mediaImportDiscoveryService,
            Logger,
            filePickerService,
            dispatcherService,
            userPreferencesService,
            fileRevealService);
    }

    public ApplicationConfiguration Configuration { get; }

    public ILogger Logger { get; }

    public MainWindow CreateMainWindow()
    {
        var window = new MainWindow(_mainViewModel)
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