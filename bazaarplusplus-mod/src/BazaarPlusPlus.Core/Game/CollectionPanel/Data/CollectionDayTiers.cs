#nullable enable
using BazaarPlusPlus.Core.Cards;

namespace BazaarPlusPlus.Game.CollectionPanel.Data;

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

public interface ICollectionDayTierSource
{
    CollectionDayTiers Resolve();
}
