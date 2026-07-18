#nullable enable
namespace BazaarPlusPlus.GameInterop.CardPreview;

internal sealed class NativeCardPreviewScopeLifetime<TResource>
    where TResource : class
{
    private readonly object _gate = new();
    private readonly Action<TResource> _returnToPool;
    private readonly Action<TResource> _destroy;
    private readonly HashSet<TResource> _active = new();
    private readonly TaskCompletionSource<object?> _closed = new(
        TaskCreationOptions.RunContinuationsAsynchronously
    );
    private int _outstandingAcquisitions;
    private bool _closing;
    private bool _closeCompleted;

    internal NativeCardPreviewScopeLifetime(
        Action<TResource> returnToPool,
        Action<TResource> destroy
    )
    {
        _returnToPool = returnToPool ?? throw new ArgumentNullException(nameof(returnToPool));
        _destroy = destroy ?? throw new ArgumentNullException(nameof(destroy));
    }

    internal bool TryBeginAcquire(out Acquisition acquisition)
    {
        lock (_gate)
        {
            if (_closing)
            {
                acquisition = null!;
                return false;
            }

            _outstandingAcquisitions++;
            acquisition = new Acquisition(this);
            return true;
        }
    }

    internal bool Release(TResource resource)
    {
        if (resource == null)
            return false;

        bool destroy;
        lock (_gate)
        {
            if (!_active.Remove(resource))
                return false;
            destroy = _closing;
        }

        if (destroy)
            _destroy(resource);
        else
            _returnToPool(resource);
        return true;
    }

    internal bool IsActive(TResource resource)
    {
        lock (_gate)
            return !_closing && _active.Contains(resource);
    }

    internal bool ForgetDestroyed(TResource resource)
    {
        if (resource == null)
            return false;
        lock (_gate)
            return _active.Remove(resource);
    }

    internal ValueTask CloseAsync()
    {
        var completeNow = false;
        lock (_gate)
        {
            if (!_closing)
                _closing = true;
            completeNow = _outstandingAcquisitions == 0 && !_closeCompleted;
        }

        if (completeNow)
            CompleteClose();
        return new ValueTask(_closed.Task);
    }

    private bool TryDeliver(Acquisition acquisition, TResource resource)
    {
        if (resource == null)
            throw new ArgumentNullException(nameof(resource));

        lock (_gate)
        {
            if (
                _closing
                || acquisition.Completed
                || acquisition.Delivered
                || !_active.Add(resource)
            )
                return false;

            acquisition.Delivered = true;
            return true;
        }
    }

    private void CompleteAcquire(Acquisition acquisition)
    {
        var completeClose = false;
        lock (_gate)
        {
            if (acquisition.Completed)
                return;

            acquisition.Completed = true;
            _outstandingAcquisitions--;
            completeClose = _closing && _outstandingAcquisitions == 0 && !_closeCompleted;
        }

        if (completeClose)
            CompleteClose();
    }

    private void CompleteClose()
    {
        List<TResource> active;
        lock (_gate)
        {
            if (_closeCompleted || !_closing || _outstandingAcquisitions != 0)
                return;

            _closeCompleted = true;
            active = new List<TResource>(_active);
            _active.Clear();
        }

        List<Exception>? failures = null;
        foreach (var resource in active)
        {
            try
            {
                _destroy(resource);
            }
            catch (Exception ex)
            {
                failures ??= new List<Exception>();
                failures.Add(ex);
            }
        }

        if (failures == null)
            _closed.TrySetResult(null);
        else
            _closed.TrySetException(new AggregateException(failures));
    }

    internal sealed class Acquisition : IDisposable
    {
        private readonly NativeCardPreviewScopeLifetime<TResource> _owner;

        internal Acquisition(NativeCardPreviewScopeLifetime<TResource> owner) => _owner = owner;

        internal bool Completed { get; set; }
        internal bool Delivered { get; set; }

        internal bool TryDeliver(TResource resource) => _owner.TryDeliver(this, resource);

        public void Dispose() => _owner.CompleteAcquire(this);
    }
}
