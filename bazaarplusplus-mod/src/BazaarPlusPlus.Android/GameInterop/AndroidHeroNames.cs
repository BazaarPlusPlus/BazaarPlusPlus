using BazaarPlusPlus.Core.Cards;
using Il2CppTheBazaar.Utilities;

namespace BazaarPlusPlus.Android.GameInterop;

/// <summary>
/// Heroes' display names from the game's own localization (<c>LocalizableShared</c>), the source
/// the desktop's <c>TheDragonsHeroIdentity</c> reads. Before the localization service is up, or
/// for a hero without a native counterpart, the facet's English name stands in.
/// </summary>
internal static class AndroidHeroNames
{
    public const string TheDragonsFallback = "The Dragons";

    /// <summary>The game's localized name for The Dragons, the port Core's hero text takes.</summary>
    public static string TheDragons() => Native(CardHero.TheDragons) ?? TheDragonsFallback;

    /// <summary>The game's localized name for <paramref name="hero"/>.</summary>
    public static string Display(CardHero hero) =>
        Native(hero) ?? (hero == CardHero.TheDragons ? TheDragonsFallback : hero.ToString());

    private static string? Native(CardHero hero)
    {
        try
        {
            if (AndroidCardFacets.Hero.TryToSource(hero, out var native))
            {
                var name = LocalizableShared.GetLocalizedHero(native);
                if (!string.IsNullOrWhiteSpace(name))
                    return name;
            }
        }
        catch (Exception)
        {
            // The localization service is not up yet: the caller's fallback stands in.
        }
        return null;
    }
}
