#nullable enable
namespace BazaarPlusPlus.Game.PvpBattles;

/// <summary>
/// One host's reads of a PvP battle for <see cref="PvpBattleCaptureWindow{TMessage}"/>: what kind
/// of net message arrived, the opening message's battle and opponent facts and boards, the live
/// player facts and boards, and an opaque replay part per stage. A read may throw or return null;
/// the window guards each field on its own. A message is valid only during the
/// <see cref="PvpBattleCaptureWindow{TMessage}.Observe"/> call that delivered it, so a host never
/// keeps one past its read (an IL2CPP proxy is serialized when it arrives).
/// </summary>
public interface IPvpBattleReads<TMessage>
    where TMessage : class
{
    PvpBattleSignal Classify(TMessage message);

    /// <summary>Read only during the observe call that delivered the opening.</summary>
    string? OpeningText(TMessage opening, PvpOpeningText field);

    /// <summary>Read only during the observe call that delivered the opening.</summary>
    int? OpeningNumber(TMessage opening, PvpOpeningNumber field);

    /// <summary>The opponent's board from the opening message's events.</summary>
    IReadOnlyList<PvpBattleCardSnapshot> OpponentBoard(
        TMessage opening,
        PvpSnapshotSection section
    );

    string? PlayerText(PvpPlayerText field);

    int? PlayerNumber(PvpPlayerNumber field);

    /// <summary>The player's board from the live game state.</summary>
    IReadOnlyList<PvpBattleCardSnapshot> PlayerBoard(PvpSnapshotSection section);

    /// <summary>
    /// The replay part of <paramref name="stage"/>: the opening game sim for
    /// <see cref="PvpReplayStage.Spawn"/>, the combat sim for <see cref="PvpReplayStage.Combat"/>,
    /// the closing game sim for <see cref="PvpReplayStage.Despawn"/>. Asked only for a PvP combat.
    /// </summary>
    PvpReplayPart KeepReplayPart(PvpReplayStage stage, TMessage message, string battleId);
}

public enum PvpBattleSignalKind
{
    Other,
    GameSim,
    CombatSim,
}

/// <summary>
/// What a net message is to the window: a game sim with its run state name, a combat sim with its
/// winner and loser combatant ids as the game names them, or anything else.
/// </summary>
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

/// <summary>The run states whose game sim opens a window.</summary>
[Flags]
public enum PvpOpeningKinds
{
    None = 0,

    /// <summary>The <c>PVPCombat</c> state: a battle against another player's board.</summary>
    PvpCombat = 1,

    /// <summary>The <c>Combat</c> state: a monster fight, captured without a replay.</summary>
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

/// <summary>
/// One message of a Replay Payload, kept from the observe call that delivered it until the window
/// closes. <see cref="Serialized"/> holds bytes taken on arrival (empty when that failed);
/// <see cref="Deferred"/> holds a producer the window runs once, at close.
/// </summary>
public abstract class PvpReplayPart
{
    private PvpReplayPart() { }

    public static PvpReplayPart Serialized(byte[] bytes) =>
        new SerializedPart(bytes ?? throw new ArgumentNullException(nameof(bytes)));

    public static PvpReplayPart Deferred(Func<byte[]> bytes) =>
        new DeferredPart(bytes ?? throw new ArgumentNullException(nameof(bytes)));

    /// <summary>A part already known to have no bytes.</summary>
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
