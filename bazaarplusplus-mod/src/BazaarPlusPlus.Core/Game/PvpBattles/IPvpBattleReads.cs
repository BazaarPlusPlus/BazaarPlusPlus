#nullable enable
namespace BazaarPlusPlus.Game.PvpBattles;

// Message reads are valid only during Observe. Android must serialize replay parts
// before the IL2CPP message proxy expires.
public interface IPvpBattleReads<TMessage>
    where TMessage : class
{
    PvpBattleSignal Classify(TMessage message);

    string? OpeningText(TMessage opening, PvpOpeningText field);

    int? OpeningNumber(TMessage opening, PvpOpeningNumber field);

    IReadOnlyList<PvpBattleCardSnapshot> OpponentBoard(
        TMessage opening,
        PvpSnapshotSection section
    );

    string? PlayerText(PvpPlayerText field);

    int? PlayerNumber(PvpPlayerNumber field);

    IReadOnlyList<PvpBattleCardSnapshot> PlayerBoard(PvpSnapshotSection section);

    // The opening message contains only changed attributes. Apply those changes to live values keyed by game member name.
    IReadOnlyDictionary<string, int>? PlayerAttributes(TMessage opening);

    // Stash cards can affect combat through quests and auras even though they appear on no board.
    IReadOnlyList<PvpBattleCardSnapshot>? PlayerStash();

    PvpReplayPart KeepReplayPart(PvpReplayStage stage, TMessage message, string battleId);
}

public enum PvpBattleSignalKind
{
    Other,
    GameSim,
    CombatSim,
}

public readonly record struct PvpBattleSignal
{
    private PvpBattleSignal(
        PvpBattleSignalKind kind,
        string? stateName,
        string? winner,
        string? loser
    )
    {
        Kind = kind;
        StateName = stateName;
        Winner = winner;
        Loser = loser;
    }

    public PvpBattleSignalKind Kind { get; }

    public string? StateName { get; }

    public string? Winner { get; }

    public string? Loser { get; }

    public static PvpBattleSignal Other => default;

    public static PvpBattleSignal GameSim(string? stateName) =>
        new(PvpBattleSignalKind.GameSim, stateName, null, null);

    public static PvpBattleSignal CombatSim(string? winner, string? loser) =>
        new(PvpBattleSignalKind.CombatSim, null, winner, loser);
}

[Flags]
public enum PvpOpeningKinds
{
    None = 0,

    PvpCombat = 1,

    MonsterCombat = 2,
}

public enum PvpOpeningText
{
    EncounterId,
    OpponentName,
    OpponentHero,
    OpponentRank,
    OpponentAccountId,
}

public enum PvpOpeningNumber
{
    Day,
    Hour,
    OpponentRating,
    OpponentLevel,
    OpponentPrestige,
    OpponentVictories,
}

public enum PvpPlayerText
{
    Hero,
    Name,
    AccountId,
}

public enum PvpPlayerNumber
{
    Level,
    Prestige,
    Income,
    Gold,
    Victories,
}

public enum PvpReplayStage
{
    Spawn,
    Combat,
    Despawn,
}

public abstract class PvpReplayPart
{
    private PvpReplayPart() { }

    public static PvpReplayPart Serialized(byte[] bytes) =>
        new SerializedPart(bytes ?? throw new ArgumentNullException(nameof(bytes)));

    public static PvpReplayPart Deferred(Func<byte[]> bytes) =>
        new DeferredPart(bytes ?? throw new ArgumentNullException(nameof(bytes)));

    internal abstract bool FailedNow { get; }

    internal abstract byte[] Resolve();

    private sealed class SerializedPart(byte[] bytes) : PvpReplayPart
    {
        internal override bool FailedNow => bytes.Length == 0;

        internal override byte[] Resolve() => bytes;
    }

    private sealed class DeferredPart(Func<byte[]> bytes) : PvpReplayPart
    {
        internal override bool FailedNow => false;

        internal override byte[] Resolve() => bytes() ?? [];
    }
}
