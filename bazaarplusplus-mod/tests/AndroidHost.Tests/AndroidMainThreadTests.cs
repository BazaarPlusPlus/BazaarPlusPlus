using BazaarPlusPlus.Android.Host;
using Xunit;

namespace AndroidHost.Tests;

/// <summary>
/// The main-thread pump History and the replay resume on: a continuation posted from a pool
/// thread runs only when the pump ticks, on the pump's thread, and an await inside it comes back
/// there too. Each test drives its own pump on a dedicated thread standing in for Unity's.
/// </summary>
public sealed class AndroidMainThreadTests
{
    [Fact]
    public async Task A_continuation_posted_from_the_pool_runs_only_on_the_pumps_tick()
    {
        using var main = new PumpThread();
        var ran = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        await Task.Run(() =>
            main.Pump.Post(_ => ran.TrySetResult(Environment.CurrentManagedThreadId), null)
        );

        Assert.False(ran.Task.IsCompleted);
        main.Tick();

        Assert.Equal(main.ThreadId, await ran.Task.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task An_await_started_in_scope_resumes_on_the_pump_after_pool_work()
    {
        using var main = new PumpThread();
        var resumedOn = new TaskCompletionSource<(bool Main, bool Nested)>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        main.Run(() =>
        {
            using var scope = main.Pump.Enter();
            _ = Flow();
        });

        async Task Flow()
        {
            await Task.Run(() => Thread.Sleep(10));
            var first = main.Pump.IsCurrentThread;
            await Task.Run(() => Thread.Sleep(10));
            resumedOn.TrySetResult((first, main.Pump.IsCurrentThread));
        }

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!resumedOn.Task.IsCompleted && DateTime.UtcNow < deadline)
        {
            main.Tick();
            await Task.Delay(5);
        }

        var result = await resumedOn.Task.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.True(result.Main);
        Assert.True(result.Nested);
    }

    [Fact]
    public void Enter_restores_the_previous_context_when_disposed()
    {
        var pump = new AndroidMainThread();
        var previous = new SynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(previous);
        try
        {
            using (pump.Enter())
                Assert.Same(pump, SynchronizationContext.Current);
            Assert.Same(previous, SynchronizationContext.Current);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(null);
        }
    }

    [Fact]
    public void A_failing_callback_is_counted_and_the_rest_still_run()
    {
        var pump = new AndroidMainThread();
        pump.Capture();
        var after = false;
        pump.Post(_ => throw new InvalidOperationException("boom"), null);
        pump.Post(_ => after = true, null);

        Assert.Equal(2, pump.Tick());
        Assert.True(after);
        Assert.Equal(1, pump.CallbackFailures);
    }

    [Fact]
    public async Task A_frame_wait_completes_when_its_condition_holds_on_a_tick()
    {
        var pump = new AndroidMainThread();
        pump.Capture();
        var ready = false;
        var wait = pump.WhenAsync(() => ready, TimeSpan.FromSeconds(30));

        pump.Tick();
        Assert.False(wait.IsCompleted);
        ready = true;
        pump.Tick();

        await wait.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.Equal(0, pump.PendingWaits);
    }

    [Fact]
    public async Task A_frame_wait_times_out_and_a_throwing_condition_faults_it()
    {
        var pump = new AndroidMainThread();
        pump.Capture();
        var timedOut = pump.WhenAsync(() => false, TimeSpan.Zero);
        var faulted = pump.WhenAsync(
            () => throw new InvalidOperationException("condition"),
            TimeSpan.FromSeconds(30)
        );

        pump.Tick();

        await Assert.ThrowsAsync<TimeoutException>(() => timedOut);
        await Assert.ThrowsAsync<InvalidOperationException>(() => faulted);
    }

    [Fact]
    public async Task Stop_drops_posted_work_and_cancels_waits_and_later_ticks_do_nothing()
    {
        var pump = new AndroidMainThread();
        pump.Capture();
        var ran = false;
        pump.Post(_ => ran = true, null);
        var wait = pump.WhenAsync(() => false, Timeout.InfiniteTimeSpan);

        pump.Stop();
        pump.Post(_ => ran = true, null);

        Assert.Equal(0, pump.Tick());
        Assert.False(ran);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => wait);
    }

    [Fact]
    public async Task Only_the_bound_thread_reads_as_the_main_thread()
    {
        var pump = new AndroidMainThread();
        Assert.False(pump.IsCurrentThread);
        pump.Capture();
        Assert.True(pump.IsCurrentThread);
        Assert.False(await Task.Run(() => pump.IsCurrentThread));
    }

    // A dedicated thread that owns a pump, as Unity's main thread owns the host's.
    private sealed class PumpThread : IDisposable
    {
        private readonly BlockingQueue _work = new();
        private readonly Thread _thread;

        public PumpThread()
        {
            var bound = new ManualResetEventSlim();
            _thread = new Thread(() =>
            {
                Pump.Capture();
                ThreadId = Environment.CurrentManagedThreadId;
                bound.Set();
                while (_work.Take() is { } action)
                    action();
            })
            {
                IsBackground = true,
            };
            _thread.Start();
            bound.Wait();
        }

        public AndroidMainThread Pump { get; } = new();

        public int ThreadId { get; private set; }

        public void Run(Action action)
        {
            using var done = new ManualResetEventSlim();
            _work.Add(() =>
            {
                try
                {
                    action();
                }
                finally
                {
                    done.Set();
                }
            });
            done.Wait();
        }

        public void Tick() => Run(() => Pump.Tick());

        public void Dispose()
        {
            _work.Add(null);
            _thread.Join(TimeSpan.FromSeconds(5));
        }
    }

    private sealed class BlockingQueue
    {
        private readonly System.Collections.Concurrent.BlockingCollection<Action?> _items = new();

        public void Add(Action? action) => _items.Add(action);

        public Action? Take() => _items.Take();
    }
}
