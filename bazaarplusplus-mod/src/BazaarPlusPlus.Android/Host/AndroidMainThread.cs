using System.Collections.Concurrent;
using System.Diagnostics;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;

namespace BazaarPlusPlus.Android.Host;

/// <summary>
/// The game's main thread as a <see cref="SynchronizationContext"/>. LemonLoader installs no
/// managed context, so an <c>await</c> started from a melon callback resumes on a pool thread,
/// where calling into IL2CPP (the client cache, <c>Time</c>, uGUI, any game object) faults the
/// process. History's coordinator and the replay runtime await without
/// <c>ConfigureAwait(false)</c> and publish on the caller's context; running their entry points
/// under <see cref="Enter"/> captures this context, so every continuation is posted here and
/// runs at the next <see cref="Tick"/>, which <c>AndroidMod.OnUpdate</c> calls every frame.
/// The context is scoped, never installed globally: the upload pump and the version check keep
/// their own pool-thread work. Each frame it also polls the waits that observe IL2CPP state
/// (<see cref="WhenAsync"/>), so a game <c>Task</c> or a condition on game objects is read on
/// this thread only.
/// </summary>
internal sealed class AndroidMainThread : SynchronizationContext
{
    private const int CallbacksPerTick = 512;
    private readonly ConcurrentQueue<(SendOrPostCallback Callback, object? State)> _posted = new();
    private readonly List<FrameWait> _waits = new();
    private readonly List<FrameWait> _waitsAdded = new();
    private readonly object _waitsLock = new();
    private int _threadId;
    private volatile bool _stopped;
    private long _callbackFailures;

    /// <summary>The host's pump; <see cref="Capture"/> binds it to the main thread.</summary>
    public static AndroidMainThread Host { get; } = new();

    public bool IsBound => Volatile.Read(ref _threadId) != 0;

    /// <summary>True on the bound thread; false before binding and on every other thread.</summary>
    public bool IsCurrentThread
    {
        get
        {
            var id = Volatile.Read(ref _threadId);
            return id != 0 && id == Environment.CurrentManagedThreadId;
        }
    }

    public int PendingCallbacks => _posted.Count;

    public int PendingWaits
    {
        get
        {
            lock (_waitsLock)
                return _waits.Count + _waitsAdded.Count;
        }
    }

    public long CallbackFailures => Interlocked.Read(ref _callbackFailures);

    /// <summary>
    /// Binds the pump to the calling thread. Called from the first <c>OnUpdate</c>, which Unity
    /// runs on its main thread; returns the previous binding (0 when unbound).
    /// </summary>
    public int Capture()
    {
        _stopped = false;
        return Interlocked.Exchange(ref _threadId, Environment.CurrentManagedThreadId);
    }

    /// <summary>
    /// Sets this context as the current one until the scope is disposed, so an <c>await</c> in
    /// it resumes on the main thread. Every History and replay entry point runs inside one.
    /// </summary>
    public Scope Enter()
    {
        var previous = Current;
        SetSynchronizationContext(this);
        return new Scope(previous);
    }

    public override void Post(SendOrPostCallback d, object? state)
    {
        ArgumentNullException.ThrowIfNull(d);
        if (_stopped)
            return;
        _posted.Enqueue((d, state));
    }

    public override void Send(SendOrPostCallback d, object? state)
    {
        ArgumentNullException.ThrowIfNull(d);
        if (IsCurrentThread)
        {
            d(state);
            return;
        }
        using var done = new ManualResetEventSlim();
        Exception? failure = null;
        Post(
            _ =>
            {
                try
                {
                    d(state);
                }
                catch (Exception exception)
                {
                    failure = exception;
                }
                finally
                {
                    done.Set();
                }
            },
            null
        );
        done.Wait();
        if (failure != null)
            throw failure;
    }

    public override SynchronizationContext CreateCopy() => this;

    /// <summary>
    /// Completes when <paramref name="condition"/> holds, read once per frame on the main
    /// thread; faults with <see cref="TimeoutException"/> past <paramref name="timeout"/> and
    /// cancels with <paramref name="cancellationToken"/>. A condition that throws faults the
    /// wait with that exception.
    /// </summary>
    public Task WhenAsync(
        Func<bool> condition,
        TimeSpan timeout,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(condition);
        var wait = new FrameWait(condition, timeout, cancellationToken);
        if (_stopped)
            wait.Completion.TrySetCanceled();
        else
            lock (_waitsLock)
                _waitsAdded.Add(wait);
        return wait.Completion.Task;
    }

    /// <summary>Completes after <paramref name="frames"/> more ticks.</summary>
    public Task FramesAsync(int frames, CancellationToken cancellationToken = default)
    {
        var remaining = Math.Max(1, frames);
        return WhenAsync(() => --remaining <= 0, Timeout.InfiniteTimeSpan, cancellationToken);
    }

    /// <summary>Completes once <paramref name="delay"/> of real time has passed.</summary>
    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken = default)
    {
        var clock = Stopwatch.StartNew();
        return WhenAsync(() => clock.Elapsed >= delay, Timeout.InfiniteTimeSpan, cancellationToken);
    }

    /// <summary>
    /// Main thread, every frame: polls the frame waits, then runs the posted callbacks, each
    /// under this context so a nested <c>await</c> resumes here too. A callback that throws is
    /// logged and the rest still run. Does nothing after <see cref="Stop"/>.
    /// </summary>
    public int Tick()
    {
        if (_stopped)
            return 0;
        using var scope = Enter();
        PollWaits();
        var ran = 0;
        while (ran < CallbacksPerTick && !_stopped && _posted.TryDequeue(out var posted))
        {
            ran++;
            try
            {
                posted.Callback(posted.State);
            }
            catch (Exception exception)
            {
                Interlocked.Increment(ref _callbackFailures);
                BppLog.ErrorEvent(
                    new BppLogEvent(
                        BppLogFeatureScope.Android,
                        "android.main_thread.callback_failed",
                        storm: ["reason_code"]
                    ),
                    exception,
                    ("reason_code", exception.GetType().Name)
                );
            }
        }
        return ran;
    }

    /// <summary>
    /// Stops the pump at shutdown: posted continuations are dropped and pending waits cancel,
    /// so nothing resumes against a torn-down panel or runtime.
    /// </summary>
    public void Stop()
    {
        _stopped = true;
        while (_posted.TryDequeue(out _)) { }
        List<FrameWait> waits;
        lock (_waitsLock)
        {
            waits = [.. _waits, .. _waitsAdded];
            _waits.Clear();
            _waitsAdded.Clear();
        }
        foreach (var wait in waits)
            wait.Completion.TrySetCanceled();
    }

    private void PollWaits()
    {
        lock (_waitsLock)
        {
            _waits.AddRange(_waitsAdded);
            _waitsAdded.Clear();
        }
        for (var index = _waits.Count - 1; index >= 0; index--)
        {
            if (_waits[index].Poll())
                _waits.RemoveAt(index);
        }
    }

    public readonly struct Scope : IDisposable
    {
        private readonly SynchronizationContext? _previous;

        internal Scope(SynchronizationContext? previous)
        {
            _previous = previous;
        }

        public void Dispose() => SetSynchronizationContext(_previous);
    }

    private sealed class FrameWait
    {
        private readonly Func<bool> _condition;
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private readonly TimeSpan _timeout;
        private readonly CancellationToken _cancellationToken;

        public FrameWait(Func<bool> condition, TimeSpan timeout, CancellationToken token)
        {
            _condition = condition;
            _timeout = timeout;
            _cancellationToken = token;
        }

        // Continuations run asynchronously: they are posted to the context the awaiting code
        // captured instead of running inside the poll loop.
        public TaskCompletionSource Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        // True once the wait is settled and can be dropped.
        public bool Poll()
        {
            if (Completion.Task.IsCompleted)
                return true;
            if (_cancellationToken.IsCancellationRequested)
            {
                Completion.TrySetCanceled(_cancellationToken);
                return true;
            }
            try
            {
                if (_condition())
                {
                    Completion.TrySetResult();
                    return true;
                }
            }
            catch (Exception exception)
            {
                Completion.TrySetException(exception);
                return true;
            }
            if (_timeout == Timeout.InfiniteTimeSpan || _clock.Elapsed < _timeout)
                return false;
            Completion.TrySetException(
                new TimeoutException(
                    $"A main-thread wait did not complete within {_timeout.TotalSeconds:0.#} s."
                )
            );
            return true;
        }
    }
}
