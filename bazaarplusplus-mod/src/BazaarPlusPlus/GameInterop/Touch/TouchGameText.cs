#nullable enable
using BazaarGameShared.Domain.Cards;
using BazaarPlusPlus.Core.Cards;
using BazaarPlusPlus.GameInterop.Cards;
using BazaarPlusPlus.GameInterop.EncounterPortraits;
using BazaarPlusPlus.GameInterop.HeroPortraits;
using BazaarPlusPlus.GameInterop.StaticCards;
using TheBazaar.Tooltips;
using TheBazaar.Utilities;
using UnityEngine;

namespace BazaarPlusPlus.GameInterop.Touch;

/// <summary>
/// Heroes' display names from the game's own localization (<c>LocalizableShared</c>), as the
/// Android host's <c>AndroidHeroNames</c>. Before the localization service is up, or for a hero
/// without a native counterpart, the facet's English name stands in.
/// </summary>
internal static class TouchHeroNames
{
    public const string TheDragonsFallback = "The Dragons";

    public static string TheDragons() => Native(CardHero.TheDragons) ?? TheDragonsFallback;

    public static string Display(CardHero hero) =>
        Native(hero) ?? (hero == CardHero.TheDragons ? TheDragonsFallback : hero.ToString());

    private static string? Native(CardHero hero)
    {
        try
        {
            if (DesktopCardFacets.Hero.TryToSource(hero, out var native))
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

/// <summary>Merchant and trainer names from the encounter template's localized title.</summary>
internal static class TouchEncounterNames
{
    public static string Display(Guid templateId, string fallback)
    {
        try
        {
            var title = BppStaticDataAccess
                .GetCardTemplate(BppStaticDataAccess.TryGetReadyManagerObject(), templateId)
                ?.Localization?.Title;
            if (title != null)
            {
                var name = title.GetLocalizedText();
                if (!string.IsNullOrWhiteSpace(name))
                    return name;
            }
        }
        catch (Exception)
        {
            // Static data and localization can be unavailable while the game loads.
        }
        return fallback;
    }
}

/// <summary>
/// Default hero and encounter art for touch rows, over the desktop's portrait providers, which
/// already cache and coalesce requests.
/// </summary>
internal static class TouchPortraits
{
    public static async Task<Sprite?> Hero(CardHero hero)
    {
        if (!DesktopCardFacets.Hero.TryToSource(hero, out var native))
            return null;
        var outcome = await HeroPortraitSpriteProvider.LoadDefaultPortraitAsync(native);
        return outcome?.Sprite;
    }

    public static async Task<Sprite?> Encounter(Guid templateId)
    {
        var outcome = await EncounterPortraitSpriteProvider.LoadPortraitAsync(templateId);
        return outcome?.Sprite;
    }
}

/// <summary>Guards against a static-data template that is not a <see cref="TCardBase"/>.</summary>
internal static class TouchTemplates
{
    public static TCardBase? Find(Guid templateId)
    {
        try
        {
            return BppStaticDataAccess.GetCardTemplate(
                BppStaticDataAccess.TryGetReadyManagerObject(),
                templateId
            );
        }
        catch (Exception)
        {
            return null;
        }
    }
}
