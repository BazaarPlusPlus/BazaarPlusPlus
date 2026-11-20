using BazaarPlusPlus.Android.Host;

namespace BazaarPlusPlus.Android.CombatReplay.Bootstrap;

internal sealed class AndroidReplayPresentationTasks<TTask>
    where TTask : class
{
    private readonly object _sync = new();
    private readonly List<TTask> _pending = new();
    private readonly Func<TTask, bool> _isCompleted;
    private readonly Func<TTask, bool> _isFaulted;
    private volatile bool _tracking;

    public AndroidReplayPresentationTasks(
        Func<TTask, bool> isCompleted,
        Func<TTask, bool> isFaulted
    )
    {
        _isCompleted = isCompleted ?? throw new ArgumentNullException(nameof(isCompleted));
        _isFaulted = isFaulted ?? throw new ArgumentNullException(nameof(isFaulted));
    }

    public bool IsTracking => _tracking;

    public IDisposable BeginTracking()
    {
        lock (_sync)
        {
            if (_tracking)
                throw new InvalidOperationException(
                    "Replay presentation task tracking is already active."
                );
            _pending.Clear();
            _tracking = true;
        }
        return new Scope(this);
    }

    public void Track(TTask? task)
    {
        if (task == null || !_tracking)
            return;
        lock (_sync)
        {
            if (_tracking)
                _pending.Add(task);
        }
    }

    public ReplayPresentationState Observe()
    {
        lock (_sync)
        {
            if (!_tracking)
                throw new InvalidOperationException(
                    "Replay presentation task tracking is not active."
                );
            if (_pending.Any(_isFaulted))
                return ReplayPresentationState.Faulted;
            _pending.RemoveAll(task => _isCompleted(task));
            return _pending.Count == 0
                ? ReplayPresentationState.Settled
                : ReplayPresentationState.Pending;
        }
    }

    public async Task WaitAsync(AndroidMainThread pump, TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(pump);
        var deadline = DateTime.UtcNow + timeout;
        while (true)
        {
            await pump.WhenAsync(
                () => Observe() != ReplayPresentationState.Pending,
                Remaining(deadline)
            );
            if (Observe() == ReplayPresentationState.Faulted)
                throw new InvalidOperationException("A replay item presentation task faulted.");
            // WaitForEndOfFrame is unverified under the loader's coroutines. Wait two frames
            // instead.
            await pump.FramesAsync(2);
            if (Observe() == ReplayPresentationState.Settled)
                return;
        }
    }

    private static TimeSpan Remaining(DateTime deadline)
    {
        var remaining = deadline - DateTime.UtcNow;
        if (remaining <= TimeSpan.Zero)
            throw new TimeoutException("Timed out while waiting for replay item presentation.");
        return remaining;
    }

    private void EndTracking()
    {
        lock (_sync)
        {
            _pending.Clear();
            _tracking = false;
        }
    }

    private sealed class Scope(AndroidReplayPresentationTasks<TTask> owner) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            owner.EndTracking();
        }
    }
}

internal enum ReplayPresentationState
{
    Pending,
    Settled,
    Faulted,
}

internal static class AndroidReplayPresentation
{
    public static AndroidReplayPresentationTasks<Il2CppSystem.Threading.Tasks.Task> Tasks { get; } =
        new(task => task.IsCompleted, task => task.IsFaulted);
}
