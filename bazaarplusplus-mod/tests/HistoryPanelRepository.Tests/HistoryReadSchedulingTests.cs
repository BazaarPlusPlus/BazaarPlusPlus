#nullable enable
using System.Collections.Concurrent;
using BazaarPlusPlus.Game.HistoryPanel;

internal static class HistoryReadSchedulingTests
{
    internal static void Run()
    {
        var previous = SynchronizationContext.Current;
        using var context = new UiContext();
        SynchronizationContext.SetSynchronizationContext(context);
        try
        {
            var reads = new LatestHistoryRead<string>();
            using var firstStarted = new ManualResetEventSlim();
            using var release = new ManualResetEventSlim();
            var calls = 0;
            var published = new List<string>();
            reads.Submit(
                () =>
                {
                    Interlocked.Increment(ref calls);
                    firstStarted.Set();
                    release.Wait();
                    return "stale";
                },
                (value, error) => published.Add(value!)
            );
            if (!firstStarted.Wait(5000))
                throw new TimeoutException();
            for (var i = 0; i < 1000; i++)
            {
                var value = i.ToString();
                reads.Submit(
                    () =>
                    {
                        Interlocked.Increment(ref calls);
                        return value;
                    },
                    (result, error) => published.Add(result!)
                );
            }
            release.Set();
            context.Until(() => published.Count == 1);
            Check(
                calls == 2 && published.Single() == "999",
                "Rapid selection must keep one running read plus only the latest pending read and never publish obsolete data."
            );
            firstStarted.Reset();
            release.Reset();
            reads.Submit(
                () =>
                {
                    firstStarted.Set();
                    release.Wait();
                    return "closed";
                },
                (value, error) => published.Add(value!)
            );
            if (!firstStarted.Wait(5000))
                throw new TimeoutException();
            reads.Clear();
            reads.Submit(
                () => throw new InvalidDataException("decode"),
                (value, error) => published.Add(error is InvalidDataException ? "failure" : "wrong")
            );
            release.Set();
            context.Until(() => published.Count == 2);
            Check(
                published.SequenceEqual(["999", "failure"]),
                "Closing must retire running results and allow the next session's error publication."
            );
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }

    private static void Check(bool value, string message)
    {
        if (!value)
            throw new InvalidOperationException(message);
    }

    private sealed class UiContext : SynchronizationContext, IDisposable
    {
        private readonly BlockingCollection<Action> _queue = new();

        public override void Post(SendOrPostCallback callback, object? state) =>
            _queue.Add(() => callback(state));

        internal void Until(Func<bool> done)
        {
            while (!done())
            {
                if (!_queue.TryTake(out var next, 5000))
                    throw new TimeoutException();
                next();
            }
        }

        public void Dispose() => _queue.Dispose();
    }
}
