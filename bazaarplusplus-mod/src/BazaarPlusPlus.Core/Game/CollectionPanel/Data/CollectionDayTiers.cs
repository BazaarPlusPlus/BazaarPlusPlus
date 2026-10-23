#nullable enable
using BazaarPlusPlus.Core.Cards;

namespace BazaarPlusPlus.Game.CollectionPanel.Data;

// The run day and the highest tier its GameData weights deal; both null outside a run or when
// the data is unavailable, which leaves the Day filter failing open.
public readonly struct CollectionDayTiers
{
    public CollectionDayTiers(int? day, CardTier? maximumTier)
    {
        Day = day;
        MaximumTier = maximumTier;
    }

    public int? Day { get; }

    public CardTier? MaximumTier { get; }
}

// Host port: each host reads its game's day-tier weights and reports them as a card facet.
public interface ICollectionDayTierSource
{
    CollectionDayTiers Resolve();
}
