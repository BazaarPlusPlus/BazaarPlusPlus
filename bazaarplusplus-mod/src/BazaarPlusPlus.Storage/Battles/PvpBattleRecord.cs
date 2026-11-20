#nullable enable
namespace BazaarPlusPlus.Game.PvpBattles.Persistence;

// Storage treats the board captures as opaque JSON. The capturing host owns their shape.
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

    public PvpBattleSnapshotDocuments? Snapshots { get; set; }
}

public sealed class PvpBattleSnapshotDocuments
{
    public string PlayerHand { get; set; } = string.Empty;

    public string PlayerSkills { get; set; } = string.Empty;

    public string OpponentHand { get; set; } = string.Empty;

    public string OpponentSkills { get; set; } = string.Empty;
}
