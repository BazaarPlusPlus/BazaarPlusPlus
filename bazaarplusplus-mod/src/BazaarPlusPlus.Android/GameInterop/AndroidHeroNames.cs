using BazaarPlusPlus.Core.Cards;
using Il2CppTheBazaar.Utilities;

namespace BazaarPlusPlus.Android.GameInterop;

internal static class AndroidHeroNames
{
    public const string TheDragonsFallback = "The Dragons";

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
