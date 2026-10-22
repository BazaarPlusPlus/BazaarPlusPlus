using System.Diagnostics;
using BazaarPlusPlus.Core.Events;
using BazaarPlusPlus.Game.CombatReplay;
using BazaarPlusPlus.Game.PvpBattles;
using BazaarPlusPlus.Game.PvpBattles.Persistence;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using Il2CppBazaarGameShared.Domain.Core.Types;
using Il2CppBazaarGameShared.Domain.Runs;
using Il2CppBazaarGameShared.Infra.Messages;
using Il2CppInterop.Runtime.InteropTypes;
using Il2CppTheBazaar;

namespace BazaarPlusPlus.Android.GameInterop;

/// <summary>
/// Records each PvP battle from the net messages, as the desktop's
/// <c>CombatReplayCaptureService</c> frames one: a <c>PVPCombat</c> game sim opens it, the combat
/// sim carries the outcome, and the next game sim closes it. The opening also takes the board
/// captures (<see cref="AndroidPvpBattleSnapshots"/>), and each of the three messages is serialized
/// as it arrives (<see cref="AndroidReplayMessages"/>). The closed battle's Replay Payload is saved
/// to the combat replay directory, then the battle through <see cref="PvpBattleSqliteStore"/>, and
/// it is announced as <see cref="PvpBattleRecorded"/>, which Run Logging links to the run. A
/// payload that could not be taken leaves the battle saved with its replay state <c>missing</c>.
/// Both saves are synchronous, so Run Logging never waits on replay persistence. Main thread only,
/// called from the message intake.
/// </summary>
internal sealed class AndroidPvpBattleCapture
{
    private const string PvpCombatKind = "PVPCombat";
    private readonly PvpBattleSqliteStore _store;
    private readonly CombatReplayPayloadStore _replays;
    private readonly IBppEventBus _eventBus;
    private readonly Func<string?> _currentRunId;
    private readonly Func<DateTimeOffset> _utcNow;
    private PvpBattleRecord? _opened;
    private PvpReplayPayload? _replay;
    private bool _combatSeen;
    private long _recorded;
    private long _replaysSaved;

    public AndroidPvpBattleCapture(
        PvpBattleSqliteStore store,
        CombatReplayPayloadStore replays,
        IBppEventBus eventBus,
        Func<string?> currentRunId,
        Func<DateTimeOffset> utcNow
    )
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _replays = replays ?? throw new ArgumentNullException(nameof(replays));
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        _currentRunId = currentRunId ?? throw new ArgumentNullException(nameof(currentRunId));
        _utcNow = utcNow ?? throw new ArgumentNullException(nameof(utcNow));
    }

    public long Recorded => Interlocked.Read(ref _recorded);

    public long ReplaysSaved => Interlocked.Read(ref _replaysSaved);

    public void Accept(INetMessage message)
    {
        var gameSim = message.TryCast<NetMessageGameSim>();
        if (gameSim != null)
        {
            AcceptGameSim(gameSim);
            return;
        }

        var combatSim = message.TryCast<NetMessageCombatSim>();
        if (combatSim != null)
            AcceptCombatSim(combatSim);
    }

    private void AcceptGameSim(NetMessageGameSim message)
    {
        var opening = IsPvpOpening(message);
        if (_opened != null && _combatSeen && !opening)
        {
            var closed = _opened;
            var replay = _replay;
            _opened = null;
            _replay = null;
            _combatSeen = false;
            if (replay != null)
                replay.DespawnMessageBytes = Serialize(message, closed.BattleId, "despawn");
            Record(closed, replay);
            return;
        }

        // An opening (re)starts the window; anything else before the combat sim abandons it,
        // as the desktop matcher does.
        _opened = opening ? Open(message) : null;
        _replay = _opened != null ? StartReplay(message, _opened.BattleId) : null;
        _combatSeen = false;
    }

    private void AcceptCombatSim(NetMessageCombatSim message)
    {
        if (_opened == null)
            return;
        if (_combatSeen)
        {
            _opened = null;
            _replay = null;
            _combatSeen = false;
            return;
        }

        _combatSeen = true;
        if (_replay != null)
            _replay.CombatMessageBytes = Serialize(message, _opened.BattleId, "combat");
        _opened.RunId ??= _currentRunId();
        var data = message.Data;
        _opened.Outcome = new PvpBattleOutcome
        {
            Result =
                data.Winner == ECombatantId.Player ? "win"
                : data.Loser == ECombatantId.Player ? "loss"
                : null,
            WinnerCombatantId = data.Winner.ToString(),
            LoserCombatantId = data.Loser.ToString(),
        };
    }

    private static bool IsPvpOpening(NetMessageGameSim message) =>
        message.Data?.CurrentState?.StateName == ERunState.PVPCombat;

    private PvpBattleRecord Open(NetMessageGameSim message)
    {
        var data = message.Data;
        var state = data.CurrentState;
        var run = Data.Run;
        var player = run?.Player;
        AndroidClientCache.TryGetRank(out var playerRank, out var playerRating);
        var record = new PvpBattleRecord
        {
            BattleId = Guid.NewGuid().ToString("N"),
            RunId = _currentRunId(),
            CombatKind = PvpCombatKind,
            Day = data.Run != null ? unchecked((int)data.Run.Day) : null,
            Hour = data.Run != null ? unchecked((int)data.Run.Hour) : null,
            EncounterId = state?.CurrentEncounterId,
            Participants = new PvpBattleParticipants
            {
                PlayerName = AndroidClientCache.TryGetProfileUsername(),
                PlayerAccountId = AndroidClientCache.TryGetProfileAccountId(),
                PlayerHero = player != null ? AndroidGameIds.Hero(player.Hero) : null,
                PlayerRank = playerRank,
                PlayerRating = playerRating,
                PlayerLevel =
                    player != null
                        ? AndroidGameIds.Attribute(player, EPlayerAttributeType.Level)
                        : null,
                PlayerPrestige =
                    player != null
                        ? AndroidGameIds.Attribute(player, EPlayerAttributeType.Prestige)
                        : null,
                PlayerIncome =
                    player != null
                        ? AndroidGameIds.Attribute(player, EPlayerAttributeType.Income)
                        : null,
                PlayerGold =
                    player != null
                        ? AndroidGameIds.Attribute(player, EPlayerAttributeType.Gold)
                        : null,
                PlayerVictories = run != null ? unchecked((int)run.Victories) : null,
            },
        };
        try
        {
            var opponent = state?.PvpOpponent ?? Data.SimPvpOpponent;
            if (opponent != null)
            {
                var participants = record.Participants;
                participants.OpponentName = Text(opponent.Name);
                participants.OpponentHero = AndroidGameIds.Hero(opponent.Hero);
                participants.OpponentRank = AndroidGameIds.Unboxed(() => opponent.Rank)?.ToString();
                participants.OpponentRating = opponent.Rating;
                participants.OpponentLevel = opponent.Level;
                participants.OpponentPrestige = opponent.Prestige;
                participants.OpponentVictories = unchecked((int)opponent.Victories);
                participants.OpponentAccountId = Text(opponent.PlayerLoadout?.accountId);
            }
        }
        catch (Exception exception)
        {
            BppLog.WarnEvent(
                new BppLogEvent(
                    BppLogFeatureScope.Android,
                    "android.pvp_battle.opponent_unreadable",
                    storm: []
                ),
                exception,
                ("battle_id", record.BattleId, BppLogCorrelationPolicy.Short)
            );
        }
        record.Snapshots = AndroidPvpBattleSnapshots.Capture(message, record.BattleId);
        return record;
    }

    private static PvpReplayPayload? StartReplay(NetMessageGameSim spawn, string battleId)
    {
        var bytes = Serialize(spawn, battleId, "spawn");
        return bytes.Length == 0
            ? null
            : new PvpReplayPayload
            {
                BattleId = battleId,
                Version = 1,
                SpawnMessageBytes = bytes,
            };
    }

    private static byte[] Serialize(NetMessageGameSim message, string battleId, string part) =>
        Serialize(message, static sim => sim.MessageId, battleId, part);

    private static byte[] Serialize(NetMessageCombatSim message, string battleId, string part) =>
        Serialize(message, static sim => sim.MessageId, battleId, part);

    // One message's bytes, or none when the game's serializer failed: the battle is still
    // recorded, without its Replay Payload.
    private static byte[] Serialize<T>(
        T message,
        Func<T, string?> messageId,
        string battleId,
        string part
    )
        where T : Il2CppObjectBase
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var bytes = AndroidReplayMessages.Serialize(message, messageId);
            BppLog.InfoEvent(
                new BppLogEvent(BppLogFeatureScope.Android, "android.replay.message_serialized"),
                ("battle_id", battleId, BppLogCorrelationPolicy.Short),
                ("part", part),
                ("bytes", bytes.Length),
                ("elapsed_ms", stopwatch.ElapsedMilliseconds)
            );
            return bytes;
        }
        catch (Exception exception)
        {
            BppLog.WarnEvent(
                new BppLogEvent(
                    BppLogFeatureScope.Android,
                    "android.replay.serialize_failed",
                    storm: ["part"]
                ),
                exception,
                ("battle_id", battleId, BppLogCorrelationPolicy.Short),
                ("part", part)
            );
            return [];
        }
    }

    // Both saves run on the main thread against the DELETE-mode database the seal and upload
    // workers also use, so the recorded event carries how long they held the frame.
    private void Record(PvpBattleRecord record, PvpReplayPayload? replay)
    {
        record.RecordedAtUtc = _utcNow();
        var saving = Stopwatch.StartNew();
        var replaySaved = SaveReplay(record, replay);
        try
        {
            _store.Save(record);
            if (!replaySaved)
                _store.MarkReplayPayloadMissing([record.BattleId], record.RecordedAtUtc);
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

        Interlocked.Increment(ref _recorded);
        BppLog.InfoEvent(
            new BppLogEvent(BppLogFeatureScope.Android, "android.pvp_battle.recorded"),
            ("battle_id", record.BattleId, BppLogCorrelationPolicy.Short),
            ("run_id", record.RunId, BppLogCorrelationPolicy.Short),
            ("day", record.Day),
            ("hour", record.Hour),
            ("result", record.Outcome.Result),
            ("replay_saved", replaySaved),
            ("save_elapsed_ms", saving.ElapsedMilliseconds)
        );
        _eventBus.Publish(
            new PvpBattleRecorded
            {
                BattleId = record.BattleId,
                RunId = record.RunId,
                CombatKind = record.CombatKind,
                Day = record.Day,
                Hour = record.Hour,
                EncounterId = record.EncounterId,
                OpponentName = record.Participants.OpponentName,
            }
        );
    }

    // Saved before the battle row, so a battle whose replay state reads ready has its file.
    private bool SaveReplay(PvpBattleRecord record, PvpReplayPayload? replay)
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
            _replays.Save(replay);
            Interlocked.Increment(ref _replaysSaved);
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

    private static string? Text(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
