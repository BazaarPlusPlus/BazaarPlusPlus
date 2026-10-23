#nullable enable
using BazaarPlusPlus.Game.PvpBattles;

namespace BazaarPlusPlus.Game.HistoryPanel.Favorites;

// A favorite's replay copy: the manifest and payload it replays from, taken once at favorite time.
// Player is the recorder in BattleManifest, as in both a local manifest and a Ghost payload's.
public sealed class FavoriteBattlePayload
{
    public string BattleId { get; set; } = string.Empty;

    public PvpBattleManifest BattleManifest { get; set; } = new();

    public PvpReplayPayload ReplayPayload { get; set; } = new();
}
