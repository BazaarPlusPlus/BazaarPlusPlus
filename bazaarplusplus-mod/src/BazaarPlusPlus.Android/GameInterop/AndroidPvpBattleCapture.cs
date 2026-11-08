using System.Diagnostics;
using BazaarPlusPlus.Android.Game;
using BazaarPlusPlus.Core.Events;
using BazaarPlusPlus.Game.CombatReplay;
using BazaarPlusPlus.Game.PvpBattles;
using BazaarPlusPlus.Game.PvpBattles.Persistence;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using Il2CppBazaarGameShared.Infra.Messages;

namespace BazaarPlusPlus.Android.GameInterop;

/// <summary>
/// Records each PvP battle Core's <see cref="PvpBattleCaptureWindow{TMessage}"/> closes over the
/// net messages, read through <see cref="AndroidPvpBattleReads"/>. The closed battle's Replay
/// Payload is saved to the combat replay directory, then the battle through
/// <see cref="PvpBattleCatalog"/>, both under the persistence lease of
/// <see cref="ReplayPayloadOperationGate"/> so the maintenance sweep never sees the file before its
/// row, and it is announced as <see cref="PvpBattleRecorded"/>, which Run Logging links to the run.
/// A payload that could not be taken leaves the battle saved with its replay state
/// <c>missing</c>. Message intake is main-thread only; detached bytes and facts are persisted by
/// <see cref="AndroidBattlePersistenceQueue"/> and results are published from the host's tick.
/// </summary>
internal sealed class AndroidPvpBattleCapture : IDisposable
{
    // Bound the wait even for the queue's synchronous capacity fallback: a replay holds the
    // gate until the main thread exits it.
    internal static readonly TimeSpan LeaseWait = TimeSpan.FromMilliseconds(200);
    private readonly PvpBattleCaptureWindow<INetMessage> _window;
    private readonly PvpBattleReadsRehearsal<INetMessage>? _rehearsal;
    private readonly PvpBattleCatalog _battles;
    private readonly CombatReplayPayloadStore _replays;
    private readonly ReplayPayloadOperationGate _replayGate;
    private readonly IBppEventBus _eventBus;
    private readonly AndroidBattlePersistenceQueue _persistence;
    private long _recorded;
    private long _replaysSaved;

    public AndroidPvpBattleCapture(
        PvpBattleCaptureWindow<INetMessage> window,
        PvpBattleReadsRehearsal<INetMessage>? rehearsal,
        PvpBattleCatalog battles,
        CombatReplayPayloadStore replays,
        ReplayPayloadOperationGate replayGate,
        IBppEventBus eventBus
    )
    {
        _window = window ?? throw new ArgumentNullException(nameof(window));
        _rehearsal = rehearsal;
        _battles = battles ?? throw new ArgumentNullException(nameof(battles));
        _replays = replays ?? throw new ArgumentNullException(nameof(replays));
        _replayGate = replayGate ?? throw new ArgumentNullException(nameof(replayGate));
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        _persistence = new(
            capture =>
                Persist(
                    _replayGate,
                    _battles,
                    _replays,
                    capture.Manifest,
                    capture.Replay,
                    LeaseWait
                ),
            OnPersisted,
            () => _eventBus.Publish(new CombatReplayPersistenceDrained())
        );
    }

    public long Recorded => Interlocked.Read(ref _recorded);

    public long ReplaysSaved => Interlocked.Read(ref _replaysSaved);

    public bool HasPendingPersistence => _persistence.HasPending;

    public IReadOnlyCollection<string> ProtectedBattleIds() => _persistence.ProtectedBattleIds();

    public void Tick()
    {
        _rehearsal?.Tick();
        _persistence.Drain();
    }

    public void Dispose() => _persistence.Dispose();

    public void Accept(INetMessage message)
    {
        var capture = _window.Observe(message);
        // The rehearsal reads only a message no battle is using, never in a battle's frame.
        if (capture == null && !_window.IsOpen)
            _rehearsal?.Offer(message);
        else
            _rehearsal?.Defer();
        if (capture != null)
        {
            if (!_persistence.Enqueue(capture))
                BppLog.WarnEvent(
                    new BppLogEvent(
                        BppLogFeatureScope.Android,
                        "android.pvp_battle.queue_full",
                        storm: ["reason_code"]
                    ),
                    ("reason_code", "synchronous_fallback")
                );
        }
    }

    private void OnPersisted(AndroidBattlePersistenceQueue.Result result, bool publish)
    {
        var capture = result.Capture;
        var record = capture.Manifest;
        if (result.Error is { } exception)
        {
            BppLog.ErrorEvent(
                new BppLogEvent(BppLogFeatureScope.Android, "android.pvp_battle.save_failed"),
                exception,
                ("battle_id", record.BattleId, BppLogCorrelationPolicy.Short),
                ("run_id", record.RunId, BppLogCorrelationPolicy.Short),
                ("save_elapsed_ms", result.ElapsedMs)
            );
            return;
        }
        var persisted = result.Outcome!.Value;
        if (persisted.ReplaySaved)
            Interlocked.Increment(ref _replaysSaved);
        if (!persisted.LeaseHeld)
            BppLog.WarnEvent(
                new BppLogEvent(
                    BppLogFeatureScope.Android,
                    "android.pvp_battle.lease_busy",
                    storm: ["reason_code"]
                ),
                ("battle_id", record.BattleId, BppLogCorrelationPolicy.Short),
                ("reason_code", "payload_gate_held"),
                ("wait_ms", (long)LeaseWait.TotalMilliseconds)
            );
        Interlocked.Increment(ref _recorded);
        BppLog.InfoEvent(
            new BppLogEvent(BppLogFeatureScope.Android, "android.pvp_battle.recorded"),
            [
                ("battle_id", record.BattleId, BppLogCorrelationPolicy.Short),
                ("run_id", record.RunId, BppLogCorrelationPolicy.Short),
                ("day", record.Day),
                ("hour", record.Hour),
                ("result", record.Outcome.Result),
                ("replay_saved", persisted.ReplaySaved),
                ("save_elapsed_ms", result.ElapsedMs),
                ("replay_elapsed_ms", persisted.ReplayElapsedMs),
                .. capture.BoardSummary,
            ]
        );
        if (publish)
            _eventBus.Publish(PvpBattleCatalog.Recorded(record));
    }

    /// <summary>
    /// Saves the Replay Payload, then the battle, under the persistence lease. A maintenance
    /// sweep or replay can hold the gate for long, so persistence uses a bounded wait even on
    /// the worker. When the lease stays
    /// held past <paramref name="leaseWait"/>, the battle is saved without its payload, its
    /// replay state <c>missing</c>, so the sweep still never sees a file before its row.
    /// </summary>
    internal static AndroidBattlePersistenceOutcome Persist(
        ReplayPayloadOperationGate gate,
        PvpBattleCatalog battles,
        CombatReplayPayloadStore replays,
        PvpBattleManifest record,
        PvpReplayPayload? replay,
        TimeSpan leaseWait
    )
    {
        using var lease = gate.TryAcquirePersistence(leaseWait);
        var replaySaving = Stopwatch.StartNew();
        var replaySaved = lease != null && SaveReplay(replays, record, replay);
        var replayElapsedMs = replaySaving.ElapsedMilliseconds;
        battles.Save(record);
        if (!replaySaved)
            battles.MarkReplayPayloadMissing([record.BattleId], record.RecordedAtUtc);
        return new AndroidBattlePersistenceOutcome(lease != null, replaySaved, replayElapsedMs);
    }

    // Saved before the battle row, so a battle whose replay state reads ready has its file.
    private static bool SaveReplay(
        CombatReplayPayloadStore replays,
        PvpBattleManifest record,
        PvpReplayPayload? replay
    )
    {
        if (
            replay == null
            || replay.SpawnMessageBytes.Length == 0
            || replay.CombatMessageBytes.Length == 0
            || replay.DespawnMessageBytes.Length == 0
        )
            return false;
        try
        {
            replays.Save(replay);
            return true;
        }
        catch (Exception exception)
        {
            BppLog.ErrorEvent(
                new BppLogEvent(BppLogFeatureScope.Android, "android.replay.save_failed"),
                exception,
                ("battle_id", record.BattleId, BppLogCorrelationPolicy.Short)
            );
            return false;
        }
    }
}
