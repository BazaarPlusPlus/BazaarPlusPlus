using System.Diagnostics;
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
/// <c>missing</c>. Both saves are synchronous, so Run Logging never waits on replay persistence.
/// Main thread only, called from the message intake.
/// </summary>
internal sealed class AndroidPvpBattleCapture
{
    // How long a capture waits for the payload gate on the main thread before it saves the
    // battle without its payload.
    internal static readonly TimeSpan LeaseWait = TimeSpan.FromMilliseconds(200);
    private readonly PvpBattleCaptureWindow<INetMessage> _window;
    private readonly PvpBattleCatalog _battles;
    private readonly CombatReplayPayloadStore _replays;
    private readonly ReplayPayloadOperationGate _replayGate;
    private readonly IBppEventBus _eventBus;
    private long _recorded;
    private long _replaysSaved;

    public AndroidPvpBattleCapture(
        PvpBattleCaptureWindow<INetMessage> window,
        PvpBattleCatalog battles,
        CombatReplayPayloadStore replays,
        ReplayPayloadOperationGate replayGate,
        IBppEventBus eventBus
    )
    {
        _window = window ?? throw new ArgumentNullException(nameof(window));
        _battles = battles ?? throw new ArgumentNullException(nameof(battles));
        _replays = replays ?? throw new ArgumentNullException(nameof(replays));
        _replayGate = replayGate ?? throw new ArgumentNullException(nameof(replayGate));
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
    }

    public long Recorded => Interlocked.Read(ref _recorded);

    public long ReplaysSaved => Interlocked.Read(ref _replaysSaved);

    public void Accept(INetMessage message)
    {
        if (_window.Observe(message) is { } capture)
            Record(capture);
    }

    // Both saves run on the main thread against the DELETE-mode database the seal and upload
    // workers also use, so the recorded event carries how long they held the frame, the lease
    // wait included, and how much of it the Replay Payload file took.
    private void Record(PvpBattleCapture capture)
    {
        var record = capture.Manifest;
        var saving = Stopwatch.StartNew();
        PersistOutcome persisted;
        try
        {
            persisted = Persist(_replayGate, _battles, _replays, record, capture.Replay, LeaseWait);
        }
        catch (Exception exception)
        {
            BppLog.ErrorEvent(
                new BppLogEvent(BppLogFeatureScope.Android, "android.pvp_battle.save_failed"),
                exception,
                ("battle_id", record.BattleId, BppLogCorrelationPolicy.Short),
                ("run_id", record.RunId, BppLogCorrelationPolicy.Short),
                ("save_elapsed_ms", saving.ElapsedMilliseconds)
            );
            return;
        }

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
                ("save_elapsed_ms", saving.ElapsedMilliseconds),
                ("replay_elapsed_ms", persisted.ReplayElapsedMs),
                .. capture.BoardSummary,
            ]
        );
        _eventBus.Publish(PvpBattleCatalog.Recorded(record));
    }

    /// <summary>
    /// Saves the Replay Payload, then the battle, under the persistence lease. The main thread
    /// never blocks on the gate: the maintenance sweep or a replay's playback can hold it for
    /// long, and this thread is the one that releases a playback lease. When the lease stays
    /// held past <paramref name="leaseWait"/>, the battle is saved without its payload, its
    /// replay state <c>missing</c>, so the sweep still never sees a file before its row.
    /// </summary>
    internal static PersistOutcome Persist(
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
        return new PersistOutcome(lease != null, replaySaved, replayElapsedMs);
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

    internal readonly record struct PersistOutcome(
        bool LeaseHeld,
        bool ReplaySaved,
        long ReplayElapsedMs
    );
}
