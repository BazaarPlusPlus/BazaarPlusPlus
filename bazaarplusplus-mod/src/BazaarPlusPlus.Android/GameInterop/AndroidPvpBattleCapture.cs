using BazaarPlusPlus.Core.Events;
using BazaarPlusPlus.Game.PvpBattles;
using BazaarPlusPlus.Game.PvpBattles.Persistence;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using Il2CppBazaarGameShared.Domain.Core.Types;
using Il2CppBazaarGameShared.Domain.Runs;
using Il2CppBazaarGameShared.Infra.Messages;
using Il2CppTheBazaar;

namespace BazaarPlusPlus.Android.GameInterop;

/// <summary>
/// Records each PvP battle from the net messages, as the desktop's
/// <c>CombatReplayCaptureService</c> frames one: a <c>PVPCombat</c> game sim opens it, the combat
/// sim carries the outcome, and the next game sim closes it. The closed battle is saved through
/// <see cref="PvpBattleSqliteStore"/> and announced as <see cref="PvpBattleRecorded"/>, which Run
/// Logging links to the run. Board captures and the Replay Payload are not taken here (#360), so
/// the battle has no snapshot row. Main thread only, called from the message intake.
/// </summary>
internal sealed class AndroidPvpBattleCapture
{
    private const string PvpCombatKind = "PVPCombat";
    private readonly PvpBattleSqliteStore _store;
    private readonly IBppEventBus _eventBus;
    private readonly Func<string?> _currentRunId;
    private readonly Func<DateTimeOffset> _utcNow;
    private PvpBattleRecord? _opened;
    private bool _combatSeen;
    private long _recorded;

    public AndroidPvpBattleCapture(
        PvpBattleSqliteStore store,
        IBppEventBus eventBus,
        Func<string?> currentRunId,
        Func<DateTimeOffset> utcNow
    )
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        _currentRunId = currentRunId ?? throw new ArgumentNullException(nameof(currentRunId));
        _utcNow = utcNow ?? throw new ArgumentNullException(nameof(utcNow));
    }

    public long Recorded => Interlocked.Read(ref _recorded);

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
            _opened = null;
            _combatSeen = false;
            Record(closed);
            return;
        }

        // An opening (re)starts the window; anything else before the combat sim abandons it,
        // as the desktop matcher does.
        _opened = opening ? Open(message) : null;
        _combatSeen = false;
    }

    private void AcceptCombatSim(NetMessageCombatSim message)
    {
        if (_opened == null)
            return;
        if (_combatSeen)
        {
            _opened = null;
            _combatSeen = false;
            return;
        }

        _combatSeen = true;
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
        return record;
    }

    private void Record(PvpBattleRecord record)
    {
        record.RecordedAtUtc = _utcNow();
        try
        {
            _store.Save(record);
        }
        catch (Exception exception)
        {
            BppLog.ErrorEvent(
                new BppLogEvent(BppLogFeatureScope.Android, "android.pvp_battle.save_failed"),
                exception,
                ("battle_id", record.BattleId, BppLogCorrelationPolicy.Short),
                ("run_id", record.RunId, BppLogCorrelationPolicy.Short)
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
            ("result", record.Outcome.Result)
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

    private static string? Text(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
