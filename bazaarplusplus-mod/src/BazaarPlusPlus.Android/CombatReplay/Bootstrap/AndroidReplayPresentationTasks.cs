using BazaarPlusPlus.Android.Host;

namespace BazaarPlusPlus.Android.CombatReplay.Bootstrap;

/// <summary>
/// The game's item setup and board spawn tasks a saved replay waits for before it plays, the
/// Android form of the desktop's <c>ReplayItemPresentationReadiness</c>. The readiness patches
/// record each IL2CPP task proxy while a replay injects (they never replace the game's task);
/// <see cref="WaitAsync"/> polls their state once per frame on the main thread, as every wait on
/// game state here does, and then waits for a render boundary. The desktop tracker stays over
/// managed tasks (ADR-0003); this one holds whatever the patches hand it, through the state
/// delegates, so its rules run in a test without IL2CPP.
/// </summary>
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

    /// <summary>Records a task while tracking; a task outside a replay is ignored.</summary>
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

    /// <summary>
    /// The pending tasks' state: completed tasks are dropped; a faulted one fails the replay's
    /// presentation wait, as a faulted task fails the desktop's <c>Task.WhenAll</c>.
    /// </summary>
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
            // The render boundary: two frames, since WaitForEndOfFrame is not proven under the
            // loader's coroutines.
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

/// <summary>What the tracked presentation tasks are doing.</summary>
internal enum ReplayPresentationState
{
    Pending,
    Settled,
    Faulted,
}

/// <summary>The tracker over the game's IL2CPP tasks the readiness patches record.</summary>
internal static class AndroidReplayPresentation
{
    public static AndroidReplayPresentationTasks<Il2CppSystem.Threading.Tasks.Task> Tasks { get; } =
        new(task => task.IsCompleted, task => task.IsFaulted);
}
