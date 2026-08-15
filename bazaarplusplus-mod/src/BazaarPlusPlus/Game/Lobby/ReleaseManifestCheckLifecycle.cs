#nullable enable
namespace BazaarPlusPlus.Game.Lobby;

internal readonly record struct ReleaseManifestCheckLease(
    int Generation,
    CancellationToken CancellationToken
);

internal sealed class ReleaseManifestCheckLifecycle : IDisposable
{
    private readonly object _sync = new();
    private CancellationTokenSource? _current;
    private IDisposable? _currentOwner;
    private int _generation;
    private bool _disposed;

    internal ReleaseManifestCheckLease Begin(IDisposable owner)
    {
        if (owner == null)
            throw new ArgumentNullException(nameof(owner));
        lock (_sync)
        {
            if (_disposed)
            {
                owner.Dispose();
                throw new ObjectDisposedException(nameof(ReleaseManifestCheckLifecycle));
            }
            _current?.Cancel();
            _current?.Dispose();
            _currentOwner?.Dispose();
            _current = new CancellationTokenSource();
            _currentOwner = owner;
            return new ReleaseManifestCheckLease(++_generation, _current.Token);
        }
    }

    internal async Task RunAsync<T>(
        ReleaseManifestCheckLease lease,
        Func<CancellationToken, Task<T>> operation,
        Action<T> publish
    )
    {
        if (operation == null)
            throw new ArgumentNullException(nameof(operation));
        if (publish == null)
            throw new ArgumentNullException(nameof(publish));
        try
        {
            var result = await operation(lease.CancellationToken).ConfigureAwait(false);
            lock (_sync)
            {
                if (IsCurrentUnsafe(lease))
                    publish(result);
            }
        }
        finally
        {
            Complete(lease);
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
                return;
            _disposed = true;
            _generation++;
            _current?.Cancel();
            _current?.Dispose();
            _currentOwner?.Dispose();
            _current = null;
            _currentOwner = null;
        }
    }

    private bool IsCurrentUnsafe(ReleaseManifestCheckLease lease) =>
        !_disposed
        && _current != null
        && !lease.CancellationToken.IsCancellationRequested
        && lease.Generation == _generation;

    private void Complete(ReleaseManifestCheckLease lease)
    {
        lock (_sync)
        {
            if (!IsCurrentUnsafe(lease))
                return;
            _current?.Dispose();
            _currentOwner?.Dispose();
            _current = null;
            _currentOwner = null;
        }
    }
}
