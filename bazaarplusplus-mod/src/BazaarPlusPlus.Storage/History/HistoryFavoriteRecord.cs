#nullable enable
namespace BazaarPlusPlus.Game.HistoryPanel.Data;

// The battle is a snapshot taken at favorite time, with its own replay copy.
public sealed class HistoryFavoriteRecord
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
