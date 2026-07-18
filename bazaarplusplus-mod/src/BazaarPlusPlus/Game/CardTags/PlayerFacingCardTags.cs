#nullable enable
using BazaarGameShared.Domain.Core.Types;

namespace BazaarPlusPlus.Game.CardTags;

// Canonical ordering for card tags that are meaningful to players. Feature-specific
// consumers may filter this list further (for example aggregate item effects only
// count item types, so Merchant is excluded there).
internal static class PlayerFacingCardTags
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
