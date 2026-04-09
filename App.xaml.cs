using System;
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
}
