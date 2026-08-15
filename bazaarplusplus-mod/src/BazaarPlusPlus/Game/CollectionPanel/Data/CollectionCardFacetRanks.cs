#nullable enable
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.GameInterop.DayTiers;

namespace BazaarPlusPlus.Game.CollectionPanel.Data;

internal static class CollectionCardFacetRanks
{
    public static int TierRank(ETier tier) => GameDataDayTierOrder.Rank(tier);

    public static int SizeRank(ECardSize size) =>
        size switch
        {
            ECardSize.Small => 0,
            ECardSize.Medium => 1,
            ECardSize.Large => 2,
            _ => 99,
        };
}
