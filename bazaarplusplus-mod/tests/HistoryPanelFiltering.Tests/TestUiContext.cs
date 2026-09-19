#nullable enable
using System.Collections.Concurrent;

internal sealed class TestUiContext : SynchronizationContext, IDisposable
{
    private readonly BlockingCollection<Action> _queue = new();

    public override void Post(SendOrPostCallback callback, object? state)
    {
        try
        {
            _queue.Add(() => callback(state));
        }
        catch (InvalidOperationException) { }
    }

    internal void Until(Func<bool> done)
    {
        while (!done())
        {
            if (!_queue.TryTake(out var next, 5000))
                throw new TimeoutException("History did not finish its read.");
            next();
        }
    }

    public void Dispose()
    {
        SynchronizationContext.SetSynchronizationContext(null);
        _queue.CompleteAdding();
    }
}
