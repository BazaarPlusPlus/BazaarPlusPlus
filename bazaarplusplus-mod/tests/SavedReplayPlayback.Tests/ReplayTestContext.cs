using System.Collections.Concurrent;

namespace BazaarPlusPlus.Tests.SavedReplays;

// Pumps worker completions on the test thread, like either game's main-thread context.
internal sealed class ReplayTestContext : SynchronizationContext, IDisposable
{
    private readonly SynchronizationContext? _previous = Current;
    private readonly int _thread = Environment.CurrentManagedThreadId;
    private readonly BlockingCollection<Action> _queue = new();
    private readonly ManualResetEventSlim _posted = new();
    private volatile bool _stopped;

    public ReplayTestContext() => SetSynchronizationContext(this);

    public override void Post(SendOrPostCallback callback, object? state)
    {
        if (_stopped)
            return;
        if (Environment.CurrentManagedThreadId == _thread)
            callback(state);
        else
        {
            _queue.Add(() => callback(state));
            _posted.Set();
        }
    }

    public T Finish<T>(Task<T> task)
    {
        while (!task.IsCompleted)
        {
            if (!_queue.TryTake(out var action, TimeSpan.FromSeconds(10)))
                throw new TimeoutException("Replay did not resume on the host context.");
            action();
        }
        return task.GetAwaiter().GetResult();
    }

    public void Stop() => _stopped = true;

    public void WaitForPost()
    {
        if (!_posted.Wait(TimeSpan.FromSeconds(10)))
            throw new TimeoutException("The worker did not post its continuation.");
    }

    public void Dispose() => SetSynchronizationContext(_previous);
}
