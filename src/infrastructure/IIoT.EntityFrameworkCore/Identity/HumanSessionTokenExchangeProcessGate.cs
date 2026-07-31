namespace IIoT.EntityFrameworkCore.Identity;

public sealed class HumanSessionTokenExchangeProcessGate
{
    private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);
    private int _waitingCount;

    internal int WaitingCount => Volatile.Read(ref _waitingCount);

    internal async ValueTask<IAsyncDisposable> EnterAsync(
        CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _waitingCount);
        try
        {
            await _gate.WaitAsync(cancellationToken);
        }
        finally
        {
            Interlocked.Decrement(ref _waitingCount);
        }

        return new Lease(_gate);
    }

    private sealed class Lease(SemaphoreSlim gate) : IAsyncDisposable
    {
        private int _released;

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                gate.Release();
            }

            return ValueTask.CompletedTask;
        }
    }
}
