using System;

namespace Vidvix.Core.Interfaces;

public interface IDispatcherService
{
    bool HasThreadAccess { get; }

    bool TryEnqueue(Action action);
}

