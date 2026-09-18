#nullable enable
namespace BazaarPlusPlus.Game.CombatReplay;

internal sealed class ReplayPayloadOperationGate : IDisposable
{
    private readonly SemaphoreSlim _exclusive = new(1, 1);
    private int _disposed;

    internal IDisposable AcquirePersistence(CancellationToken cancellationToken) =>
        Acquire(cancellationToken);

    internal IDisposable AcquireMaintenance(CancellationToken cancellationToken) =>
        Acquire(cancellationToken);

    internal IDisposable? TryAcquirePlayback()
    {
        ThrowIfDisposed();
        return _exclusive.Wait(0) ? new Lease(_exclusive) : null;
    }

    private IDisposable Acquire(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        _exclusive.Wait(cancellationToken);
        return new Lease(_exclusive);
    }

    public void Dispose()
    {
        // A bounded shutdown can leave a cancellation-aware lease unwinding on a worker.
        // Keep the tiny semaphore alive so that late lease release stays safe.
        Interlocked.Exchange(ref _disposed, 1);
    }

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposed) == 1)
            throw new ObjectDisposedException(nameof(ReplayPayloadOperationGate));
    }

    private sealed class Lease : IDisposable
    {
        private SemaphoreSlim? _owner;

        internal Lease(SemaphoreSlim owner)
        {
            _owner = owner;
        }

        public void Dispose()
        {
            Interlocked.Exchange(ref _owner, null)?.Release();
        }
    }
}
