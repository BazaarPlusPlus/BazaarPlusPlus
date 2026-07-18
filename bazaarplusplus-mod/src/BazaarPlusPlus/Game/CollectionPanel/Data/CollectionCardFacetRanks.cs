#nullable enable
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Game.Encounters;

namespace BazaarPlusPlus.Game.CollectionPanel.Data;

internal static class CollectionCardFacetRanks
{
    public static int TierRank(ETier tier) => TierOrder.Rank(tier);

    public static int SizeRank(ECardSize size) =>
        size switch
        {
            ECardSize.Small => 0,
            ECardSize.Medium => 1,
            ECardSize.Large => 2,
            _ => 99,
        };
}
