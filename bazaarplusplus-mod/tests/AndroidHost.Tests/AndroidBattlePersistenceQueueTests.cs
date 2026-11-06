using System.Collections.Concurrent;
using BazaarPlusPlus.Android.Game;
using BazaarPlusPlus.Game.CombatReplay;
using BazaarPlusPlus.Game.PvpBattles;
using Xunit;

namespace AndroidHost.Tests;

public sealed class AndroidBattlePersistenceQueueTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [Fact]
    public void Intake_does_not_wait_for_storage_and_publication_keeps_tracking_until_observers_finish()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var capture = Capture();
        var caller = Environment.CurrentManagedThreadId;
        var published = false;
        var drained = false;
        AndroidBattlePersistenceQueue? queue = null;
        using (
            queue = new(
                value =>
                {
                    Assert.NotEqual(caller, Environment.CurrentManagedThreadId);
                    Assert.Same(capture, value);
                    entered.Set();
                    Assert.True(release.Wait(Timeout));
                    return new(true, true, 1);
                },
                (result, publish) =>
                {
                    Assert.True(publish);
                    Assert.Equal(caller, Environment.CurrentManagedThreadId);
                    Assert.Null(result.Error);
                    Assert.Null(result.Capture.Replay);
                    Assert.Equal(capture.Manifest.RunId, result.Capture.Manifest.RunId);
                    Assert.True(queue!.HasPending);
                    Assert.True(ReplayPersistenceStateTracker.HasPending(capture.Manifest.RunId!));
                    Assert.Contains(capture.Manifest.BattleId, queue.ProtectedBattleIds());
                    published = true;
                },
                () =>
                {
                    Assert.True(published);
                    Assert.False(queue!.HasPending);
                    Assert.False(ReplayPersistenceStateTracker.HasPending(capture.Manifest.RunId!));
                    drained = true;
                }
            )
        )
        {
            try
            {
                Assert.True(queue.Enqueue(capture));
                Assert.True(entered.Wait(Timeout));
                queue.Drain();
                Assert.False(published);
                Assert.True(queue.HasPending);
            }
            finally
            {
                release.Set();
            }
            DrainUntil(queue, () => drained);
            Assert.Empty(queue.ProtectedBattleIds());
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Capacity_fallback_publishes_after_earlier_work_without_losing_battles(
        bool byteLimit
    )
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var first = Capture();
        var fallback = Capture();
        if (byteLimit)
            fallback.Replay!.CombatMessageBytes = new byte[32 * 1024 * 1024];
        var expected = new List<string> { first.Manifest.BattleId };
        var actual = new List<string>();
        var fallbackThread = 0;
        Task<bool>? fallbackSave = null;
        var fallbackCaller = 0;
        using var queue = new AndroidBattlePersistenceQueue(
            capture =>
            {
                if (capture == first)
                {
                    entered.Set();
                    Assert.True(release.Wait(Timeout));
                }
                if (capture == fallback)
                    fallbackThread = Environment.CurrentManagedThreadId;
                return new(true, true, 0);
            },
            (result, _) => actual.Add(result.Capture.Manifest.BattleId),
            () => { }
        );
        try
        {
            Assert.True(queue.Enqueue(first));
            Assert.True(entered.Wait(Timeout));
            if (!byteLimit)
                for (var i = 1; i < 16; i++)
                {
                    var capture = Capture();
                    expected.Add(capture.Manifest.BattleId);
                    Assert.True(queue.Enqueue(capture));
                }
            expected.Add(fallback.Manifest.BattleId);
            fallbackSave = Task.Run(() =>
            {
                fallbackCaller = Environment.CurrentManagedThreadId;
                return queue.Enqueue(fallback);
            });
            Assert.True(
                SpinWait.SpinUntil(
                    () => queue.ProtectedBattleIds().Count == expected.Count,
                    Timeout
                )
            );
            Assert.False(fallbackSave.IsCompleted);
            Assert.Equal(0, fallbackThread);
            queue.Drain();
            Assert.Empty(actual);
            Assert.Equal(expected.Count, queue.ProtectedBattleIds().Count);
        }
        finally
        {
            release.Set();
        }
        Assert.False(await fallbackSave!.WaitAsync(Timeout));
        Assert.Equal(fallbackCaller, fallbackThread);
        DrainUntil(queue, () => !queue.HasPending);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Storage_and_observer_failures_release_tracking_and_do_not_stop_the_next_battle()
    {
        var failed = Capture();
        var next = Capture();
        var published = new List<AndroidBattlePersistenceQueue.Result>();
        var drained = false;
        using var queue = new AndroidBattlePersistenceQueue(
            capture =>
                capture == failed ? throw new IOException("disk failed") : new(true, false, 0),
            (result, _) =>
            {
                published.Add(result);
                if (result.Capture.Manifest.BattleId == failed.Manifest.BattleId)
                    throw new InvalidOperationException("observer failed");
            },
            () => drained = true
        );
        queue.Enqueue(failed);
        queue.Enqueue(next);
        DrainUntil(queue, () => drained);
        Assert.Equal(2, published.Count);
        Assert.IsType<IOException>(published[0].Error);
        Assert.Null(published[1].Error);
        Assert.False(published[1].Outcome!.Value.ReplaySaved);
        Assert.False(ReplayPersistenceStateTracker.HasPending(failed.Manifest.RunId!));
        Assert.False(ReplayPersistenceStateTracker.HasPending(next.Manifest.RunId!));
    }

    [Fact]
    public void Shutdown_keeps_accepted_writes_but_suppresses_late_game_events()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var first = Capture();
        var next = Capture();
        var persisted = new ConcurrentQueue<string>();
        var results = new ConcurrentQueue<bool>();
        var drained = 0;
        using var queue = new AndroidBattlePersistenceQueue(
            capture =>
            {
                entered.Set();
                Assert.True(release.Wait(Timeout));
                persisted.Enqueue(capture.Manifest.BattleId);
                return new(true, true, 0);
            },
            (_, publish) => results.Enqueue(publish),
            () => Interlocked.Increment(ref drained)
        );
        try
        {
            queue.Enqueue(first);
            queue.Enqueue(next);
            Assert.True(entered.Wait(Timeout));
            queue.Dispose();
            Assert.True(queue.HasPending);
            Assert.Throws<ObjectDisposedException>(() => queue.Enqueue(Capture()));
        }
        finally
        {
            release.Set();
        }
        Assert.True(SpinWait.SpinUntil(() => !queue.HasPending, Timeout));
        Assert.Equal(new[] { first.Manifest.BattleId, next.Manifest.BattleId }, persisted);
        Assert.Equal(new[] { false, false }, results);
        Assert.Equal(0, drained);
        Assert.False(ReplayPersistenceStateTracker.HasPending(first.Manifest.RunId!));
        Assert.False(ReplayPersistenceStateTracker.HasPending(next.Manifest.RunId!));
    }

    private static void DrainUntil(AndroidBattlePersistenceQueue queue, Func<bool> done) =>
        Assert.True(
            SpinWait.SpinUntil(
                () =>
                {
                    queue.Drain();
                    return done();
                },
                Timeout
            )
        );

    private static PvpBattleCapture Capture()
    {
        var id = Guid.NewGuid().ToString("N");
        return new(
            new PvpBattleManifest { BattleId = id, RunId = "run-" + id },
            ReplayMessages.Payload(id),
            []
        );
    }
}
