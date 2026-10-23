#nullable enable
namespace BazaarPlusPlus.Game.CombatReplay;

/// <summary>
/// One exclusive lease over a host's Replay Payload files: saving a battle, the maintenance sweep,
/// and playback never overlap, so the sweep never sees a payload whose battle row is not saved yet.
/// </summary>
public sealed class ReplayPayloadOperationGate : IDisposable
{
    private readonly SemaphoreSlim _exclusive = new(1, 1);
    private int _disposed;

    public IDisposable AcquirePersistence(CancellationToken cancellationToken) =>
        Acquire(cancellationToken);

    public IDisposable AcquireMaintenance(CancellationToken cancellationToken) =>
        Acquire(cancellationToken);

    public IDisposable? TryAcquirePlayback()
    {
        ThrowIfDisposed();
        return _exclusive.Wait(0) ? new Lease(_exclusive) : null;
    }

    /// <summary>
    /// The persistence lease for a caller that must not block, such as a game's main thread:
    /// null when the gate stays held for <paramref name="timeout"/>.
    /// </summary>
    public IDisposable? TryAcquirePersistence(TimeSpan timeout)
    {
        ThrowIfDisposed();
        return _exclusive.Wait(timeout) ? new Lease(_exclusive) : null;
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
