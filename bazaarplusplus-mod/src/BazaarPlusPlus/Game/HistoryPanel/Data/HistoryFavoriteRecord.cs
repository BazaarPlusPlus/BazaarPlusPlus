#nullable enable
namespace BazaarPlusPlus.Game.HistoryPanel.Data;

// A battle the player kept in History's Favorites. Battle carries the snapshot taken at favorite
// time: Source is where it came from (Local or Ghost) and Replay is the favorite's own copy.
internal sealed class HistoryFavoriteRecord
{
    public HistoryFavoriteRecord(
        HistoryBattleRecord battle,
        DateTimeOffset favoritedAtUtc,
        string note
    )
    {
        Battle = battle ?? throw new ArgumentNullException(nameof(battle));
        FavoritedAtUtc = favoritedAtUtc;
        Note = note ?? string.Empty;
    }

    public HistoryBattleRecord Battle { get; }

    public string BattleId => Battle.BattleId;

    public DateTimeOffset FavoritedAtUtc { get; }

    public string Note { get; }
}
