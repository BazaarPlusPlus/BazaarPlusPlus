using System.Diagnostics;
using BazaarPlusPlus.Game.CombatReplay;
using BazaarPlusPlus.Game.PvpBattles;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;

namespace BazaarPlusPlus.Android.Game;

/// <summary>
/// Persists detached battle facts on one worker. Results, including a capacity fallback, publish
/// in capture order on the caller's thread. No delegate run by the worker may enter IL2CPP.
/// </summary>
internal sealed class AndroidBattlePersistenceQueue : IDisposable
{
    private const int MaximumQueuedBattles = 16;
    private const long MaximumReplayBytes = 32 * 1024 * 1024;
    private static readonly TimeSpan ShutdownDrain = TimeSpan.FromMilliseconds(500);
    private readonly object _gate = new();
    private readonly object _drainGate = new();
    private readonly object _writeGate = new();
    private readonly Queue<Work> _pending = new();
    private readonly Dictionary<long, string> _protected = new();
    private readonly SortedDictionary<long, Result> _completed = new();
    private readonly Func<PvpBattleCapture, AndroidBattlePersistenceOutcome> _persist;
    private readonly Action<Result, bool> _publish;
    private readonly Action _drained;
    private Task? _worker;
    private long _nextSequence;
    private long _nextPublication;
    private long _replayBytes;
    private int _queuedBattles;
    private bool _stopping;
    private bool _lateResults;

    public AndroidBattlePersistenceQueue(
        Func<PvpBattleCapture, AndroidBattlePersistenceOutcome> persist,
        Action<Result, bool> publish,
        Action drained
    )
    {
        _persist = persist;
        _publish = publish;
        _drained = drained;
    }

    public bool HasPending
    {
        get
        {
            lock (_gate)
                return _protected.Count != 0;
        }
    }

    public IReadOnlyCollection<string> ProtectedBattleIds()
    {
        lock (_gate)
            return _protected.Values.ToArray();
    }

    /// <returns>False when capacity required the existing synchronous persistence path.</returns>
    public bool Enqueue(PvpBattleCapture capture)
    {
        Work work;
        bool queued;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_stopping, this);
            var replay = capture.Replay;
            var bytes =
                replay == null
                    ? 0L
                    : (long)replay.SpawnMessageBytes.Length
                        + replay.CombatMessageBytes.Length
                        + replay.DespawnMessageBytes.Length;
            queued =
                _queuedBattles < MaximumQueuedBattles && _replayBytes + bytes <= MaximumReplayBytes;
            work = new(_nextSequence++, capture, queued ? bytes : 0);
            ReplayPersistenceStateTracker.Enqueued(capture.Manifest.RunId);
            _protected.Add(work.Sequence, capture.Manifest.BattleId);
            if (queued)
            {
                _replayBytes += bytes;
                _queuedBattles++;
                _pending.Enqueue(work);
                _worker ??= Task.Run(Process);
            }
        }
        if (!queued)
            Persist(work, queued: false);
        return queued;
    }

    public void Drain() => Drain(publish: true);

    private void Process()
    {
        while (true)
        {
            Work work;
            lock (_gate)
            {
                if (!_pending.TryDequeue(out work!))
                {
                    _worker = null;
                    return;
                }
            }
            Persist(work, queued: true);
        }
    }

    private void Persist(Work work, bool queued)
    {
        var clock = Stopwatch.StartNew();
        Exception? error = null;
        AndroidBattlePersistenceOutcome? outcome = null;
        try
        {
            // A capacity fallback must not race our worker for the payload lease: a slow write
            // would otherwise make the next valid replay time out and be marked missing.
            lock (_writeGate)
                outcome = _persist(work.Capture);
        }
        catch (Exception exception)
        {
            error = exception;
        }
        bool late;
        lock (_gate)
        {
            // Completed results retain no message arrays, so memory credit is returned here.
            _completed.Add(
                work.Sequence,
                new(work.Capture with { Replay = null }, outcome, error, clock.ElapsedMilliseconds)
            );
            if (queued)
            {
                _replayBytes -= work.Bytes;
                _queuedBattles--;
            }
            late = _lateResults;
        }
        if (late)
            Drain(publish: false);
    }

    private void Drain(bool publish)
    {
        lock (_drainGate)
        {
            var processed = false;
            while (true)
            {
                Result result;
                long sequence;
                lock (_gate)
                {
                    sequence = _nextPublication;
                    if (!_completed.Remove(sequence, out result!))
                        break;
                    _nextPublication++;
                }
                try
                {
                    _publish(result, publish);
                }
                catch (Exception exception)
                {
                    ReportObserverFailure(exception);
                }
                finally
                {
                    // Recorded observers still see persistence pending until publication ends.
                    ReplayPersistenceStateTracker.Completed(result.Capture.Manifest.RunId);
                    lock (_gate)
                        _protected.Remove(sequence);
                    processed = true;
                }
            }
            if (publish && processed && !HasPending)
            {
                try
                {
                    _drained();
                }
                catch (Exception exception)
                {
                    ReportObserverFailure(exception);
                }
            }
        }
    }

    public void Dispose()
    {
        Task? worker;
        lock (_gate)
        {
            if (_stopping)
                return;
            _stopping = true;
            worker = _worker;
        }
        worker?.Wait(ShutdownDrain);
        lock (_drainGate)
        {
            Drain(publish: true);
            lock (_gate)
                _lateResults = true;
            // Catch a result that arrived between the normal drain and switching modes.
            Drain(publish: false);
        }
        // Accepted writes continue after a bounded shutdown. Late results release tracking only;
        // they never publish to the stopped event bus or call a game's runtime object.
    }

    private sealed record Work(long Sequence, PvpBattleCapture Capture, long Bytes);

    private static void ReportObserverFailure(Exception exception) =>
        BppLog.ErrorEvent(
            new BppLogEvent(BppLogFeatureScope.Android, "android.pvp_battle.publish_failed"),
            exception
        );

    internal sealed record Result(
        PvpBattleCapture Capture,
        AndroidBattlePersistenceOutcome? Outcome,
        Exception? Error,
        long ElapsedMs
    );
}

internal readonly record struct AndroidBattlePersistenceOutcome(
    bool LeaseHeld,
    bool ReplaySaved,
    long ReplayElapsedMs
);
