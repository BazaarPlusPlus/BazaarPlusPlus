#nullable enable

namespace BazaarPlusPlus.Game.CombatReplay.Bootstrap;

internal sealed class ReplayPresentationTaskTracker
{
    private readonly object _sync = new();
    private readonly HashSet<Task> _pending = [];
    private bool _tracking;

    internal IDisposable BeginTracking()
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

        return new TrackingScope(this);
    }

    internal Task Track(Task task)
    {
        if (task == null)
            throw new ArgumentNullException(nameof(task));

        lock (_sync)
        {
            if (!_tracking)
                return task;

            _pending.Add(task);
            return task;
        }
    }

    internal Task[] SnapshotPending()
    {
        lock (_sync)
        {
            if (!_tracking)
                throw new InvalidOperationException(
                    "Replay presentation task tracking is not active."
                );

            _pending.RemoveWhere(task => task.IsCompletedSuccessfully);
            return _pending.ToArray();
        }
    }

    private void EndTracking()
    {
        lock (_sync)
        {
            _pending.Clear();
            _tracking = false;
        }
    }

    private sealed class TrackingScope(ReplayPresentationTaskTracker tracker) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
                return;

            tracker.EndTracking();
            _disposed = true;
        }
    }
}
