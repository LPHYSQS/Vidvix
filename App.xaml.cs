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

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var dispatcherQueue = DispatcherQueue.GetForCurrentThread()
            ?? throw new InvalidOperationException("The UI dispatcher queue is not available.");

        _compositionRoot ??= new AppCompositionRoot(dispatcherQueue);
        _window = _compositionRoot.CreateMainWindow();
        _window.Activate();
    }

    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        _compositionRoot?.Logger.Log(LogLevel.Error, "An unhandled application exception occurred.", e.Exception);
    }
}
