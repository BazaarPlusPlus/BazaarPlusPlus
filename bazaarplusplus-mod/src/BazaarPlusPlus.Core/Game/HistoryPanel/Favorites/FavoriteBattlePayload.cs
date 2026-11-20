#nullable enable
using BazaarPlusPlus.Game.PvpBattles;

namespace BazaarPlusPlus.Game.HistoryPanel.Favorites;

// Player identifies the recorder in both local and Ghost Battle manifests.
public sealed class FavoriteBattlePayload
{
    public string BattleId { get; set; } = string.Empty;

    public PvpBattleManifest BattleManifest { get; set; } = new();

    public PvpReplayPayload ReplayPayload { get; set; } = new();
}
