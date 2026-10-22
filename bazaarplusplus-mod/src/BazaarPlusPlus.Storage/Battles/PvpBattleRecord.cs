#nullable enable
namespace BazaarPlusPlus.Game.PvpBattles.Persistence;

/// <summary>
/// One local PvP battle as the history database stores it: a <c>battles</c> row and, when the
/// host captured boards, its <c>battle_snapshots</c> row. Storage keeps the four board captures
/// as the JSON documents a host wrote; the host that reads game cards owns their shape.
/// </summary>
public sealed class PvpBattleRecord
{
    public string BattleId { get; set; } = string.Empty;

    public string? RunId { get; set; }

    public DateTimeOffset RecordedAtUtc { get; set; }

    public string? CombatKind { get; set; }

    public int? Day { get; set; }

    public int? Hour { get; set; }

    public string? EncounterId { get; set; }

    public PvpBattleParticipants Participants { get; set; } = new();

    public PvpBattleOutcome Outcome { get; set; } = new();

    /// <summary>The board captures; null when the host captured none, so no snapshot row exists.</summary>
    public PvpBattleSnapshotDocuments? Snapshots { get; set; }
}

/// <summary>The four board captures of a battle, each a JSON document the capturing host wrote.</summary>
public sealed class PvpBattleSnapshotDocuments
{
    public string PlayerHand { get; set; } = string.Empty;

    public string PlayerSkills { get; set; } = string.Empty;

    public string OpponentHand { get; set; } = string.Empty;

    public string OpponentSkills { get; set; } = string.Empty;
}
