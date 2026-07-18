#nullable enable
using BazaarGameShared.Domain.Core.Types;

namespace BazaarPlusPlus.Game.Encounters;

internal static class TierOrder
{
    internal static int Rank(ETier tier) =>
        tier switch
        {
            ETier.Bronze => 0,
            ETier.Silver => 1,
            ETier.Gold => 2,
            ETier.Diamond => 3,
            ETier.Legendary => 4,
            _ => 99,
        };
}
