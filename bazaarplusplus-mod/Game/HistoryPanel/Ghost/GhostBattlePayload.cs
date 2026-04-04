#nullable enable
using BazaarPlusPlus.Game.PvpBattles;

namespace BazaarPlusPlus.Game.HistoryPanel.Ghost;

internal sealed class GhostBattlePayload
{
    public string BattleId { get; set; } = string.Empty;

    public PvpBattleManifest BattleManifest { get; set; } = new();

    public PvpReplayPayload ReplayPayload { get; set; } = new();
}
