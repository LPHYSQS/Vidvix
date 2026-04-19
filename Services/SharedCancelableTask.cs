using System;
using System.Threading;
using System.Threading.Tasks;

namespace Vidvix.Services;

internal sealed class SharedCancelableTask<TResult> : IDisposable
{
    private readonly CancellationTokenSource _cancellationSource = new();
    private readonly Task<TResult> _task;
    private int _waiterCount;
    private int _disposeState;
    private int _cancelState;

    public SharedCancelableTask(Func<CancellationToken, Task<TResult>> taskFactory)
    {
        ArgumentNullException.ThrowIfNull(taskFactory);
        _task = taskFactory(_cancellationSource.Token);
    }

    public Task<TResult> Task => _task;

    public void AddWaiter() => Interlocked.Increment(ref _waiterCount);

    public int ReleaseWaiter() => Interlocked.Decrement(ref _waiterCount);

    public void Cancel()
    {
        if (Interlocked.Exchange(ref _cancelState, 1) != 0)
        {
            return;
        }

        try
        {
            _cancellationSource.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) != 0)
        {
            return;
        }

        _cancellationSource.Dispose();
    }
}
