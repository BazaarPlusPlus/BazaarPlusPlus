#nullable enable
using BazaarPlusPlus.Game.PvpBattles;

namespace BazaarPlusPlus.Game.HistoryPanel.Ghost;

public sealed class GhostBattlePayload
{
    public string BattleId { get; set; } = string.Empty;

    public int PerspectiveVersion { get; set; }

    public PvpBattleManifest BattleManifest { get; set; } = new();

    public PvpReplayPayload ReplayPayload { get; set; } = new();
}
