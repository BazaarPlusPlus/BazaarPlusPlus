#nullable enable
using System.Globalization;
using BazaarPlusPlus.Core.GameState;
using BazaarPlusPlus.Core.RunContext;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;

namespace BazaarPlusPlus.Game.PvpBattles;

// Hosts must call Observe from a single thread.
public sealed class PvpBattleCaptureWindow<TMessage>
    where TMessage : class
{
    internal const string PvpCombatState = "PVPCombat";
    internal const string MonsterCombatState = "Combat";
    internal const string Win = "win";
    internal const string Loss = "loss";
    internal const string PlayerCombatant = "Player";

    private readonly IPvpBattleReads<TMessage> _reads;
    private readonly IRunContext _run;
    private readonly IRunSnapshotProbe _runSnapshot;
    private readonly Func<DateTimeOffset> _utcNow;
    private readonly PvpOpeningKinds _openingKinds;
    private OpenBattle? _open;

    public PvpBattleCaptureWindow(
        IPvpBattleReads<TMessage> reads,
        IRunContext run,
        IRunSnapshotProbe runSnapshot,
        Func<DateTimeOffset> utcNow,
        PvpOpeningKinds openingKinds
    )
    {
        _reads = reads ?? throw new ArgumentNullException(nameof(reads));
        _run = run ?? throw new ArgumentNullException(nameof(run));
        _runSnapshot = runSnapshot ?? throw new ArgumentNullException(nameof(runSnapshot));
        _utcNow = utcNow ?? throw new ArgumentNullException(nameof(utcNow));
        _openingKinds = openingKinds;
    }

    public bool IsOpen => _open != null;

    public PvpBattleCapture? Observe(TMessage message)
    {
        if (message == null)
            return null;
        try
        {
            if (!TryClassify(message, out var signal))
                return null;
            var kind = OpeningKind(signal);
            switch (Transition(_open != null, _open?.CombatSeen == true, signal.Kind, kind))
            {
                case WindowStep.Open:
                    // Cleared first, so a throw while opening never leaves the previous battle.
                    _open = null;
                    _open = Open(message, signal.StateName, kind);
                    return null;
                case WindowStep.Abandon:
                    _open = null;
                    return null;
                case WindowStep.SeeCombat:
                    SeeCombat(_open!, message, signal);
                    return null;
                case WindowStep.Close:
                    // Reset before the close reads, so a failure among them never leaves the
                    // window waiting to close on every later game sim.
                    var closing = _open!;
                    _open = null;
                    return Close(closing, message);
                default:
                    return null;
            }
        }
        catch (Exception exception)
        {
            _open = null;
            BppLog.ErrorEvent(
                new BppLogEvent(BppLogFeatureScope.PvpBattles, "pvp_battles.window.observe_failed"),
                exception
            );
            return null;
        }
    }

    internal enum WindowStep
    {
        None,
        Open,
        Abandon,
        SeeCombat,
        Close,
    }

    internal static WindowStep Transition(
        bool open,
        bool combatSeen,
        PvpBattleSignalKind signal,
        PvpOpeningKinds openingKind
    ) =>
        signal switch
        {
            PvpBattleSignalKind.GameSim when openingKind != PvpOpeningKinds.None => WindowStep.Open,
            PvpBattleSignalKind.GameSim when !open => WindowStep.None,
            PvpBattleSignalKind.GameSim => combatSeen ? WindowStep.Close : WindowStep.Abandon,
            PvpBattleSignalKind.CombatSim when !open => WindowStep.None,
            PvpBattleSignalKind.CombatSim => combatSeen ? WindowStep.Abandon : WindowStep.SeeCombat,
            _ => WindowStep.None,
        };

    private PvpOpeningKinds OpeningKind(PvpBattleSignal signal)
    {
        if (signal.Kind != PvpBattleSignalKind.GameSim)
            return PvpOpeningKinds.None;
        var kind = signal.StateName switch
        {
            PvpCombatState => PvpOpeningKinds.PvpCombat,
            MonsterCombatState => PvpOpeningKinds.MonsterCombat,
            _ => PvpOpeningKinds.None,
        };
        return (_openingKinds & kind) != 0 ? kind : PvpOpeningKinds.None;
    }

    private bool TryClassify(TMessage message, out PvpBattleSignal signal)
    {
        try
        {
            signal = _reads.Classify(message);
            return true;
        }
        catch (Exception exception)
        {
            BppLog.WarnEvent(
                new BppLogEvent(
                    BppLogFeatureScope.PvpBattles,
                    "pvp_battles.window.classify_failed",
                    storm: []
                ),
                exception
            );
            signal = PvpBattleSignal.Other;
            return false;
        }
    }

    private OpenBattle Open(TMessage opening, string? stateName, PvpOpeningKinds kind)
    {
        var battle = new OpenBattle(
            Guid.NewGuid().ToString("N"),
            kind == PvpOpeningKinds.PvpCombat,
            stateName
        );
        var battleId = battle.BattleId;
        battle.RunId = Text(
            () => _run.CurrentServerRunId,
            PvpBattleReadSide.Battle,
            "RunId",
            battleId
        );
        battle.EncounterId = OpeningText(opening, PvpOpeningText.EncounterId, battle);
        battle.Day = OpeningNumber(opening, PvpOpeningNumber.Day, battle);
        battle.Hour = OpeningNumber(opening, PvpOpeningNumber.Hour, battle);

        var participants = battle.Participants;
        participants.PlayerHero = PlayerText(PvpPlayerText.Hero, battleId);
        participants.PlayerLevel = PlayerNumber(PvpPlayerNumber.Level, battleId);
        participants.PlayerPrestige = PlayerNumber(PvpPlayerNumber.Prestige, battleId);
        participants.PlayerIncome = PlayerNumber(PvpPlayerNumber.Income, battleId);
        participants.PlayerGold = PlayerNumber(PvpPlayerNumber.Gold, battleId);
        participants.PlayerVictories = PlayerNumber(PvpPlayerNumber.Victories, battleId);
        ReadRank(participants, battleId);
        participants.OpponentName = OpeningText(opening, PvpOpeningText.OpponentName, battle);
        participants.OpponentHero = OpeningText(opening, PvpOpeningText.OpponentHero, battle);
        participants.OpponentRank = OpeningText(opening, PvpOpeningText.OpponentRank, battle);
        participants.OpponentAccountId = OpeningText(
            opening,
            PvpOpeningText.OpponentAccountId,
            battle
        );
        participants.OpponentRating = OpeningNumber(
            opening,
            PvpOpeningNumber.OpponentRating,
            battle
        );
        participants.OpponentLevel = OpeningNumber(opening, PvpOpeningNumber.OpponentLevel, battle);
        participants.OpponentPrestige = OpeningNumber(
            opening,
            PvpOpeningNumber.OpponentPrestige,
            battle
        );
        participants.OpponentVictories = OpeningNumber(
            opening,
            PvpOpeningNumber.OpponentVictories,
            battle
        );

        ReadOpening(
            battle.PlayerHand,
            () => _reads.PlayerBoard(PvpSnapshotSection.Hand),
            PvpSnapshotReasonCode.OpeningDataException,
            battleId
        );
        ReadOpening(
            battle.PlayerSkills,
            () => _reads.PlayerBoard(PvpSnapshotSection.Skills),
            PvpSnapshotReasonCode.OpeningDataException,
            battleId
        );
        ReadOpening(
            battle.OpponentHand,
            () => _reads.OpponentBoard(opening, PvpSnapshotSection.Hand),
            PvpSnapshotReasonCode.OpeningMessageException,
            battleId
        );
        ReadOpening(
            battle.OpponentSkills,
            () => _reads.OpponentBoard(opening, PvpSnapshotSection.Skills),
            PvpSnapshotReasonCode.OpeningMessageException,
            battleId
        );

        KeepPart(battle, PvpReplayStage.Spawn, opening);
        if (battle.Pvp)
        {
            battle.PlayerAttributes = PlayerAttributes(opening, battleId);
            battle.PlayerStash = PlayerStash(battleId);
        }
        return battle;
    }

    private void SeeCombat(OpenBattle battle, TMessage combat, PvpBattleSignal signal)
    {
        battle.CombatSeen = true;
        RetryLive(battle);
        KeepPart(battle, PvpReplayStage.Combat, combat);
        battle.RunId ??= Text(
            () => _run.CurrentServerRunId,
            PvpBattleReadSide.Battle,
            "RunId",
            battle.BattleId
        );
        battle.Outcome = new PvpBattleOutcome
        {
            Result =
                signal.Winner == PlayerCombatant ? Win
                : signal.Loser == PlayerCombatant ? Loss
                : null,
            WinnerCombatantId = signal.Winner,
            LoserCombatantId = signal.Loser,
        };
    }

    private PvpBattleCapture Close(OpenBattle battle, TMessage closing)
    {
        var battleId = battle.BattleId;
        KeepPart(battle, PvpReplayStage.Despawn, closing);
        RetryLive(battle);
        battle.Participants.PlayerName = PlayerText(PvpPlayerText.Name, battleId);
        battle.Participants.PlayerAccountId = PlayerText(PvpPlayerText.AccountId, battleId);
        // A battle can open before the run ID is available, so read it again when closing.
        var closingRunId = Text(
            () => _run.CurrentServerRunId,
            PvpBattleReadSide.Battle,
            "RunId",
            battleId
        );
        var manifest = new PvpBattleManifest
        {
            BattleId = battleId,
            RunId = closingRunId ?? battle.RunId,
            RecordedAtUtc = _utcNow(),
            CombatKind = battle.CombatKind,
            Day = battle.Day,
            Hour = battle.Hour,
            EncounterId = battle.EncounterId,
            Participants = battle.Participants,
            Outcome = battle.Outcome,
            Snapshots = new PvpBattleSnapshots
            {
                PlayerHand = battle.PlayerHand.Capture(),
                PlayerSkills = battle.PlayerSkills.Capture(),
                OpponentHand = battle.OpponentHand.Capture(),
                OpponentSkills = battle.OpponentSkills.Capture(),
            },
        };
        BppLogField[] summary =
        [
            battle.PlayerHand.Summary(),
            battle.PlayerSkills.Summary(),
            battle.OpponentHand.Summary(),
            battle.OpponentSkills.Summary(),
        ];
        return new PvpBattleCapture(manifest, ResolveReplay(battle), summary);
    }

    private void ReadRank(PvpBattleParticipants participants, string battleId)
    {
        try
        {
            if (!_runSnapshot.TryGetRankSnapshot(out var rank) || rank == null)
                return;
            participants.PlayerRank = Normalize(rank.Rank);
            participants.PlayerRating = rank.Rating;
        }
        catch (Exception exception)
        {
            ReportUnreadable(PvpBattleReadSide.Player, "Rank", battleId, exception);
        }
    }

    private string? OpeningText(TMessage opening, PvpOpeningText field, OpenBattle battle) =>
        Text(
            () => _reads.OpeningText(opening, field),
            SideOf(field),
            field.ToString(),
            battle.BattleId
        );

    private int? OpeningNumber(TMessage opening, PvpOpeningNumber field, OpenBattle battle) =>
        Number(
            () => _reads.OpeningNumber(opening, field),
            field is PvpOpeningNumber.Day or PvpOpeningNumber.Hour
                ? PvpBattleReadSide.Battle
                : PvpBattleReadSide.Opponent,
            field.ToString(),
            battle.BattleId
        );

    private string? PlayerText(PvpPlayerText field, string battleId) =>
        Text(() => _reads.PlayerText(field), PvpBattleReadSide.Player, field.ToString(), battleId);

    private int? PlayerNumber(PvpPlayerNumber field, string battleId) =>
        Number(
            () => _reads.PlayerNumber(field),
            PvpBattleReadSide.Player,
            field.ToString(),
            battleId
        );

    private Dictionary<string, int>? PlayerAttributes(TMessage opening, string battleId)
    {
        try
        {
            var attributes = _reads.PlayerAttributes(opening);
            return attributes == null ? null : new Dictionary<string, int>(attributes);
        }
        catch (Exception exception)
        {
            ReportUnreadable(PvpBattleReadSide.Player, "Attributes", battleId, exception);
            return null;
        }
    }

    private List<PvpBattleCardDocument>? PlayerStash(string battleId)
    {
        try
        {
            var stash = _reads.PlayerStash();
            return stash == null ? null : PvpBattleSnapshotsCodec.ToCardDocuments(stash);
        }
        catch (Exception exception)
        {
            ReportUnreadable(PvpBattleReadSide.Player, "Stash", battleId, exception);
            return null;
        }
    }

    private static PvpBattleReadSide SideOf(PvpOpeningText field) =>
        field == PvpOpeningText.EncounterId ? PvpBattleReadSide.Battle : PvpBattleReadSide.Opponent;

    private static string? Text(
        Func<string?> read,
        PvpBattleReadSide side,
        string field,
        string battleId
    )
    {
        try
        {
            return Normalize(read());
        }
        catch (Exception exception)
        {
            ReportUnreadable(side, field, battleId, exception);
            return null;
        }
    }

    private static int? Number(
        Func<int?> read,
        PvpBattleReadSide side,
        string field,
        string battleId
    )
    {
        try
        {
            return read();
        }
        catch (Exception exception)
        {
            ReportUnreadable(side, field, battleId, exception);
            return null;
        }
    }

    internal static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;

    private static void ReportUnreadable(
        PvpBattleReadSide side,
        string field,
        string battleId,
        Exception exception
    ) =>
        BppLog.WarnEvent(
            new BppLogEvent(
                BppLogFeatureScope.PvpBattles,
                "pvp_battles.participant.unreadable",
                storm: ["side", "field"]
            ),
            exception,
            ("side", side),
            ("field", field),
            ("battle_id", battleId, BppLogCorrelationPolicy.Short)
        );

    private static void ReadOpening(
        CardSet set,
        Func<IReadOnlyList<PvpBattleCardSnapshot>> read,
        PvpSnapshotReasonCode reason,
        string battleId
    )
    {
        if (!TryReadBoard(set, read, reason, battleId, out var items))
            return;
        set.Items = items;
        set.FromOpening = true;
        set.OpeningCount = items.Count;
    }

    private void RetryLive(OpenBattle battle)
    {
        RetryLive(
            battle.PlayerHand,
            () => _reads.PlayerBoard(PvpSnapshotSection.Hand),
            battle.BattleId
        );
        RetryLive(
            battle.PlayerSkills,
            () => _reads.PlayerBoard(PvpSnapshotSection.Skills),
            battle.BattleId
        );
    }

    private static void RetryLive(
        CardSet set,
        Func<IReadOnlyList<PvpBattleCardSnapshot>> read,
        string battleId
    )
    {
        if (!ShouldRetryLive(set.FromOpening, set.Live, set.Items.Count))
            return;
        set.Live = TryReadBoard(
            set,
            read,
            PvpSnapshotReasonCode.LiveReadException,
            battleId,
            out var items
        );
        set.Items = items;
    }

    private static bool TryReadBoard(
        CardSet set,
        Func<IReadOnlyList<PvpBattleCardSnapshot>> read,
        PvpSnapshotReasonCode reason,
        string battleId,
        out List<PvpBattleCardSnapshot> items
    )
    {
        try
        {
            items = (read() ?? []).Where(card => card != null).ToList();
            return true;
        }
        catch (Exception exception)
        {
            BppLog.WarnEvent(
                new BppLogEvent(
                    BppLogFeatureScope.PvpBattles,
                    "pvp_battles.snapshot.degraded",
                    storm: ["combatant", "section", "reason_code"]
                ),
                exception,
                ("combatant", set.Combatant),
                ("section", set.Section),
                ("battle_id", battleId, BppLogCorrelationPolicy.Short),
                ("reason_code", reason)
            );
            items = [];
            return false;
        }
    }

    internal static bool ShouldRetryLive(bool capturedFromOpening, bool capturedLive, int count) =>
        !capturedLive && (!capturedFromOpening || count == 0);

    internal static (PvpBattleCaptureStatus Status, PvpBattleCaptureSource Source) Label(
        bool capturedFromOpening,
        bool capturedLive,
        int count
    )
    {
        if (!capturedFromOpening && !capturedLive)
            return (PvpBattleCaptureStatus.Missing, PvpBattleCaptureSource.Unknown);
        var status =
            count == 0 ? PvpBattleCaptureStatus.CapturedEmpty : PvpBattleCaptureStatus.Captured;
        return (
            status,
            capturedFromOpening
                ? PvpBattleCaptureSource.OpeningMessage
                : PvpBattleCaptureSource.LiveRetry
        );
    }

    // Replay parts must be contiguous and ordered by stage.
    private void KeepPart(OpenBattle battle, PvpReplayStage stage, TMessage message)
    {
        if (!battle.Pvp || battle.PartFailed)
            return;
        try
        {
            var part = _reads.KeepReplayPart(stage, message, battle.BattleId);
            if (part == null || part.FailedNow)
            {
                FailPart(battle, stage, exception: null);
                return;
            }
            battle.Parts[(int)stage] = part;
        }
        catch (Exception exception)
        {
            FailPart(battle, stage, exception);
        }
    }

    private static PvpReplayPayload? ResolveReplay(OpenBattle battle)
    {
        if (!battle.Pvp || battle.PartFailed)
            return null;
        var bytes = new byte[battle.Parts.Length][];
        for (var index = 0; index < battle.Parts.Length; index++)
        {
            var stage = (PvpReplayStage)index;
            try
            {
                var resolved = battle.Parts[index]?.Resolve() ?? [];
                if (resolved.Length == 0)
                {
                    FailPart(battle, stage, exception: null);
                    return null;
                }
                bytes[index] = resolved;
            }
            catch (Exception exception)
            {
                FailPart(battle, stage, exception);
                return null;
            }
        }
        return new PvpReplayPayload
        {
            BattleId = battle.BattleId,
            Version = 1,
            SpawnMessageBytes = bytes[(int)PvpReplayStage.Spawn],
            CombatMessageBytes = bytes[(int)PvpReplayStage.Combat],
            DespawnMessageBytes = bytes[(int)PvpReplayStage.Despawn],
            PlayerAttributes = battle.PlayerAttributes,
            PlayerStash = battle.PlayerStash,
        };
    }

    private static void FailPart(OpenBattle battle, PvpReplayStage stage, Exception? exception)
    {
        battle.PartFailed = true;
        var logEvent = new BppLogEvent(
            BppLogFeatureScope.PvpBattles,
            "pvp_battles.replay.part_failed",
            storm: ["stage"]
        );
        if (exception != null)
            BppLog.WarnEvent(
                logEvent,
                exception,
                ("stage", stage),
                ("battle_id", battle.BattleId, BppLogCorrelationPolicy.Short)
            );
        else
            BppLog.WarnEvent(
                logEvent,
                ("stage", stage),
                ("battle_id", battle.BattleId, BppLogCorrelationPolicy.Short)
            );
    }

    private sealed class OpenBattle(string battleId, bool pvp, string? combatKind)
    {
        public string BattleId { get; } = battleId;

        public bool Pvp { get; } = pvp;

        public string? CombatKind { get; } = combatKind;

        public string? RunId { get; set; }

        public string? EncounterId { get; set; }

        public int? Day { get; set; }

        public int? Hour { get; set; }

        public bool CombatSeen { get; set; }

        public Dictionary<string, int>? PlayerAttributes { get; set; }

        public List<PvpBattleCardDocument>? PlayerStash { get; set; }

        public PvpBattleParticipants Participants { get; } = new();

        public PvpBattleOutcome Outcome { get; set; } = new();

        public CardSet PlayerHand { get; } =
            new("player_hand", PvpSnapshotCombatant.Player, PvpSnapshotSection.Hand);

        public CardSet PlayerSkills { get; } =
            new("player_skills", PvpSnapshotCombatant.Player, PvpSnapshotSection.Skills);

        public CardSet OpponentHand { get; } =
            new("opponent_hand", PvpSnapshotCombatant.Opponent, PvpSnapshotSection.Hand);

        public CardSet OpponentSkills { get; } =
            new("opponent_skills", PvpSnapshotCombatant.Opponent, PvpSnapshotSection.Skills);

        public PvpReplayPart?[] Parts { get; } = new PvpReplayPart?[3];

        public bool PartFailed { get; set; }
    }

    private sealed class CardSet(
        string label,
        PvpSnapshotCombatant combatant,
        PvpSnapshotSection section
    )
    {
        public PvpSnapshotCombatant Combatant { get; } = combatant;

        public PvpSnapshotSection Section { get; } = section;

        public List<PvpBattleCardSnapshot> Items { get; set; } = [];

        public bool FromOpening { get; set; }

        public bool Live { get; set; }

        public int? OpeningCount { get; set; }

        public PvpBattleCardSetCapture Capture()
        {
            var (status, source) = Label(FromOpening, Live, Items.Count);
            return new PvpBattleCardSetCapture
            {
                Items = Items.Select(card => card.Clone()).ToList(),
                Status = status,
                Source = source,
            };
        }

        public BppLogField Summary()
        {
            var (status, source) = Label(FromOpening, Live, Items.Count);
            var opening = OpeningCount?.ToString(CultureInfo.InvariantCulture) ?? "failed";
            return new BppLogField(
                label,
                $"{status}/{source}/{Items.Count}/opening={opening}{(Live ? "/live" : "")}"
            );
        }
    }
}
