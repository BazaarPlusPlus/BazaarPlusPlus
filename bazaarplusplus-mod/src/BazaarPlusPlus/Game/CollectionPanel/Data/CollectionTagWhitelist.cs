#nullable enable
using System.Collections.Generic;
using BazaarGameShared.Domain.Core.Types;

namespace BazaarPlusPlus.Game.CollectionPanel.Data;

// Player-facing ECardTag filter options, ordered to mirror BazaarDB's Types & Tags type slice.
// Event/combat/system-only tags stay out; catalog availability then removes options that current
// game data does not actually use.
internal static class CollectionTagWhitelist
{
    public static readonly IReadOnlyList<ECardTag> Ordered = new[]
    {
        ECardTag.Weapon,
        ECardTag.Friend,
        ECardTag.Aquatic,
        ECardTag.Tool,
        ECardTag.Drone,
        ECardTag.Vehicle,
        ECardTag.Food,
        ECardTag.Trap,
        ECardTag.Toy,
        ECardTag.Potion,
        ECardTag.Reagent,
        ECardTag.Relic,
        ECardTag.Dragon,
        ECardTag.Core,
        ECardTag.Tech,
        ECardTag.Dinosaur,
        ECardTag.Ray,
        ECardTag.Apparel,
        ECardTag.Merchant,
        ECardTag.Property,
        ECardTag.Loot,
    };
}
