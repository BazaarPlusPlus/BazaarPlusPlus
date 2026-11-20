#nullable enable
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.GameInterop.Heroes;
using BazaarPlusPlus.Infrastructure.DesktopKit;
using UnityEngine;

namespace BazaarPlusPlus.GameInterop.HeroPortraits;

// Costumes, ornaments and silhouettes can fool automatic face crops, so these squares are
// chosen by eye.
internal static class HeroPortraitFocus
{
    internal static Color Glow(EHero hero) => HeroVisual.Resolve(hero.ToString()).Background;

    internal static KitPortraitFocus? For(EHero hero) =>
        hero.ToString() switch
        {
            "Pygmalien" => new KitPortraitFocus(.50f, .26f, .52f),
            "Vanessa" => new KitPortraitFocus(.48f, .30f, .46f),
            "Dooley" => new KitPortraitFocus(.50f, .42f, .72f),
            "Jules" => new KitPortraitFocus(.53f, .25f, .48f),
            "Stelle" => new KitPortraitFocus(.55f, .27f, .46f),
            "Mak" => new KitPortraitFocus(.52f, .25f, .46f),
            // The spear's tip is the top of the art, so the rule finds a head above the face.
            "Karnok" => new KitPortraitFocus(.45f, .37f, .54f),
            // Two heads: the square holds both faces.
            _ when TheDragonsHeroIdentity.IsTheDragons(hero) => new KitPortraitFocus(
                .52f,
                .36f,
                .62f
            ),
            _ => null,
        };
}
