using System;
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

    public AppCompositionRoot(DispatcherQueue dispatcherQueue)
    {
        ArgumentNullException.ThrowIfNull(dispatcherQueue);

        Configuration = new ApplicationConfiguration();
        Logger = new SimpleLogger(Configuration.MirrorLogsToConsole);

        _windowContext = new WindowContext();
        var dispatcherService = new DispatcherService(dispatcherQueue);
        var filePickerService = new FilePickerService(_windowContext);
        var ffmpegService = new FFmpegService(Logger);
        var commandBuilder = new FFmpegCommandBuilder(Configuration.FFmpegExecutablePath);

        _mainViewModel = new MainViewModel(
            Configuration,
            ffmpegService,
            commandBuilder,
            Logger,
            filePickerService,
            dispatcherService);
    }

    public ApplicationConfiguration Configuration { get; }

    public ILogger Logger { get; }

    public MainWindow CreateMainWindow()
    {
        var window = new MainWindow(_mainViewModel);
        _windowContext.SetWindow(window);
        return window;
    }
}
