using System;
using System.Threading;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace Vidvix;

internal static class Program
{
    private const string SingleInstanceMutexName = @"Local\Vidvix.SingleInstance";
    private const string ActivateExistingInstanceEventName = @"Local\Vidvix.ActivateExistingInstance";
    private static readonly object RedirectedActivationSyncRoot = new();
    private static Action? _redirectedActivationHandler;
    private static bool _hasPendingRedirectedActivation;
    private static Mutex? _singleInstanceMutex;
    private static EventWaitHandle? _activateExistingInstanceEvent;
    private static RegisteredWaitHandle? _activateExistingInstanceRegistration;

    [STAThread]
    private static int Main(string[] args)
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();

        if (!TryOwnSingleInstance())
        {
            SignalExistingInstance();
            return 0;
        }

        Application.Start(_ =>
        {
            var dispatcherQueue = DispatcherQueue.GetForCurrentThread()
                ?? throw new InvalidOperationException("当前线程缺少可用的界面调度队列。");
            var context = new DispatcherQueueSynchronizationContext(dispatcherQueue);
            SynchronizationContext.SetSynchronizationContext(context);
            new App();
        });

        return 0;
    }

    public static void RegisterRedirectedActivationHandler(Action handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        var shouldInvokePendingActivation = false;
        lock (RedirectedActivationSyncRoot)
        {
            _redirectedActivationHandler = handler;
            if (_hasPendingRedirectedActivation)
            {
                _hasPendingRedirectedActivation = false;
                shouldInvokePendingActivation = true;
            }
        }

        if (shouldInvokePendingActivation)
        {
            handler();
        }
    }

    private static bool TryOwnSingleInstance()
    {
        _singleInstanceMutex = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out var ownsSingleInstance);
        if (!ownsSingleInstance)
        {
            return false;
        }

        _activateExistingInstanceEvent = new EventWaitHandle(
            initialState: false,
            mode: EventResetMode.AutoReset,
            name: ActivateExistingInstanceEventName);

        _activateExistingInstanceRegistration = ThreadPool.RegisterWaitForSingleObject(
            _activateExistingInstanceEvent,
            static (_, timedOut) =>
            {
                if (!timedOut)
                {
                    OnActivationSignalReceived();
                }
            },
            null,
            Timeout.Infinite,
            executeOnlyOnce: false);

        return true;
    }

    private static void SignalExistingInstance()
    {
        using var activateExistingInstanceEvent = new EventWaitHandle(
            initialState: false,
            mode: EventResetMode.AutoReset,
            name: ActivateExistingInstanceEventName);
        activateExistingInstanceEvent.Set();
    }

    private static void OnActivationSignalReceived()
    {
        Action? activationHandler;
        lock (RedirectedActivationSyncRoot)
        {
            activationHandler = _redirectedActivationHandler;
            if (activationHandler is null)
            {
                _hasPendingRedirectedActivation = true;
                return;
            }
        }

        activationHandler();
    }
}
