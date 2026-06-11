#nullable enable
using System;

namespace BazaarPlusPlus.Game.PvpBattles;

public sealed class PvpBattleManifest
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

    public PvpBattleSnapshots Snapshots { get; set; } = new();
}
