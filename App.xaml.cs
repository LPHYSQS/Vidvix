using System;
using System.IO;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Vidvix.Core.Models;
using Vidvix.Utils;

namespace Vidvix;

public partial class App : Application
{
    private Window? _window;
    private AppCompositionRoot? _compositionRoot;

    public App()
    {
        PreparePublishedRuntimeEnvironment();
        InitializeComponent();
        UnhandledException += OnUnhandledException;
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        var dispatcherQueue = DispatcherQueue.GetForCurrentThread()
            ?? throw new InvalidOperationException("当前线程缺少可用的界面调度队列。");

        _compositionRoot ??= new AppCompositionRoot(dispatcherQueue);
        _window = _compositionRoot.CreateMainWindow();

        if (_window is Views.MainWindow mainWindow)
        {
            mainWindow.ApplyInitialWindowPlacementBeforeActivate();
        }

        _window.Activate();

        try
        {
            await _compositionRoot.InitializeAsync();
        }
        catch (Exception exception)
        {
            _compositionRoot.Logger.Log(LogLevel.Error, "应用启动初始化失败。", exception);
        }
    }

    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        _compositionRoot?.Logger.Log(LogLevel.Error, "应用发生未处理异常。", e.Exception);
    }

    private static void PreparePublishedRuntimeEnvironment()
    {
        var baseDirectory = ApplicationPaths.RuntimeBaseDirectoryPath;
        if (string.IsNullOrWhiteSpace(baseDirectory) || !Directory.Exists(baseDirectory))
        {
            return;
        }

        Environment.SetEnvironmentVariable("MICROSOFT_WINDOWSAPPRUNTIME_BASE_DIRECTORY", baseDirectory);

        var executableDirectory = ApplicationPaths.ExecutableDirectoryPath;
        if (!string.Equals(Environment.CurrentDirectory, executableDirectory, StringComparison.OrdinalIgnoreCase))
        {
            Directory.SetCurrentDirectory(executableDirectory);
        }

        PrependProcessPath(executableDirectory);
        PrependProcessPath(Path.Combine(executableDirectory, "Tools", "mpv"));
        PrependProcessPath(Path.Combine(executableDirectory, "Tools", "ffmpeg"));
    }

    private static void PrependProcessPath(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return;
        }

        var currentPath = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        var segments = currentPath.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        foreach (var segment in segments)
        {
            if (string.Equals(segment, directory, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        var updatedPath = string.IsNullOrWhiteSpace(currentPath)
            ? directory
            : string.Concat(directory, Path.PathSeparator, currentPath);
        Environment.SetEnvironmentVariable("PATH", updatedPath);
    }
}
