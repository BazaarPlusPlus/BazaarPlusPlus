#nullable enable
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Infrastructure.DesktopKit;

namespace BazaarPlusPlus.GameInterop.HeroPortraits;

/// <summary>
/// Where each hero's face is in the default portrait, for the Desktop UI kit's portrait wells.
/// The busts differ: a chef's hat, a hair ornament or a round body moves the face away from
/// where a rule finds it, so each hero has a square chosen by eye. A hero without one (a new
/// hero) gets the kit's rule.
/// </summary>
internal static class HeroPortraitFocus
{
    /// <summary>The hero's face square, or null to let the kit find the head.</summary>
    internal static KitPortraitFocus? For(EHero hero) =>
        hero.ToString() switch
        {
            "Pygmalien" => new KitPortraitFocus(.50f, .26f, .52f),
            "Vanessa" => new KitPortraitFocus(.48f, .30f, .46f),
            "Dooley" => new KitPortraitFocus(.50f, .42f, .72f),
            "Jules" => new KitPortraitFocus(.53f, .25f, .48f),
            "Stelle" => new KitPortraitFocus(.55f, .27f, .46f),
            "Mak" => new KitPortraitFocus(.52f, .25f, .46f),
            _ => null,
        };
}
