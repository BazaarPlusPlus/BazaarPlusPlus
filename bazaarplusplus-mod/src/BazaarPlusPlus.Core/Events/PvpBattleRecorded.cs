#nullable enable
namespace BazaarPlusPlus.Core.Events;

// Publish after committing the battle so Run Logging can attach the persisted row.
public sealed class PvpBattleRecorded
{
    public string BattleId { get; init; } = string.Empty;

    public string? RunId { get; init; }

    public string? CombatKind { get; init; }

    public int? Day { get; init; }

    public int? Hour { get; init; }

    public string? EncounterId { get; init; }

    public string? OpponentName { get; init; }
}
