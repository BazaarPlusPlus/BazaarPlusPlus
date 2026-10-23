#nullable enable

namespace BazaarPlusPlus.Game.CombatReplay;

public sealed class CombatReplayPlaybackEnded
{
    public string BattleId { get; set; } = string.Empty;

    public string? Reason { get; set; }

    public bool Failed { get; set; }
}
