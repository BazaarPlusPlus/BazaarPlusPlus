#nullable enable
using BazaarGameShared.Domain.Core.Types;

namespace BazaarPlusPlus.Game.CollectionPanel.Data;

internal static class CollectionCardFacetRanks
{
    public static int TierRank(ETier tier) =>
        tier switch
        {
            ETier.Bronze => 0,
            ETier.Silver => 1,
            ETier.Gold => 2,
            ETier.Diamond => 3,
            ETier.Legendary => 4,
            _ => 99,
        };

    public static int SizeRank(ECardSize size) =>
        size switch
        {
            ECardSize.Small => 0,
            ECardSize.Medium => 1,
            ECardSize.Large => 2,
            _ => 99,
        };
}
