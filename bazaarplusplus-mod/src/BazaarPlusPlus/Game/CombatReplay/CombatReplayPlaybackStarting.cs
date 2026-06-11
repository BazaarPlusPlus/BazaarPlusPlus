#nullable enable
using BazaarPlusPlus.Game.PvpBattles;

namespace BazaarPlusPlus.Game.CombatReplay;

internal sealed class CombatReplayPlaybackStarting
{
    public string BattleId { get; set; } = string.Empty;

    public PvpBattleManifest? Manifest { get; set; }

    public CombatReplayPlaybackSource Source { get; set; }

    public bool RecordVideo { get; set; }
}
