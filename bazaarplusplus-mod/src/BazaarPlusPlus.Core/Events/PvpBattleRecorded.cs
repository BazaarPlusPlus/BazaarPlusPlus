#nullable enable
namespace BazaarPlusPlus.Core.Events;

/// <summary>
/// Published after a host saved a local PvP battle to the history database: the facts Run
/// Logging attaches to the run, copied from the host's capture.
/// </summary>
public sealed class PvpBattleRecorded
{
    public string BattleId { get; init; } = string.Empty;

    /// <summary>The run the capture saw; null when it was captured before the run id was known.</summary>
    public string? RunId { get; init; }

    public string? CombatKind { get; init; }

    public int? Day { get; init; }

    public int? Hour { get; init; }

    public string? EncounterId { get; init; }

    public string? OpponentName { get; init; }
}
