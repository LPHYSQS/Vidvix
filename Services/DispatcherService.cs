using System;
using Microsoft.UI.Dispatching;
using Vidvix.Core.Interfaces;

namespace Vidvix.Services;

public sealed class DispatcherService : IDispatcherService
{
    private readonly DispatcherQueue _dispatcherQueue;

    public DispatcherService(DispatcherQueue dispatcherQueue)
    {
        _dispatcherQueue = dispatcherQueue;
    }

    public bool HasThreadAccess => _dispatcherQueue.HasThreadAccess;

    public bool TryEnqueue(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (_dispatcherQueue.HasThreadAccess)
        {
            action();
            return true;
        }

        return _dispatcherQueue.TryEnqueue(() => action());
    }
}
