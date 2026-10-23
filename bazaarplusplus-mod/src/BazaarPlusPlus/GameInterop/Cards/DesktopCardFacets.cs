#nullable enable
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Core.Cards;
using BazaarPlusPlus.Core.GameState;
using BazaarPlusPlus.GameInterop.Heroes;

namespace BazaarPlusPlus.GameInterop.Cards;

/// <summary>
/// The desktop host's card facet adapter (ADR-0015): the Mono game enums mapped to BPP card
/// facets by member name. The Android host has the same adapter over the interop enums.
/// </summary>
internal static class DesktopCardFacets
{
    public static readonly CardFacetNameMap<EHero, CardHero> Hero = new(
        new Dictionary<string, CardHero>(StringComparer.OrdinalIgnoreCase)
        {
            [TheDragonsHeroId.LegacyId] = CardHero.TheDragons,
        }
    );

    public static readonly CardFacetNameMap<ETier, CardTier> Tier = new();

    public static readonly CardFacetNameMap<ECardSize, CardSize> Size = new();

    public static readonly CardFacetNameMap<ECardType, CardType> Type = new();

    public static readonly CardFacetNameMap<ECardTag, CardTag> Tag = new();

    public static readonly CardFacetNameMap<EHiddenTag, CardHiddenTag> HiddenTag = new();

    public static readonly CardFacetNameMap<EEnchantmentType, CardEnchantment> Enchantment = new();

    public static bool IsHeroAvailable(CardHero hero) => Hero.TryToSource(hero, out _);

    public static string TheDragonsDisplayName() =>
        Hero.TryToSource(CardHero.TheDragons, out var hero)
            ? TheDragonsHeroIdentity.ResolveDisplayName(hero)
            : TheDragonsHeroIdentity.FallbackDisplayName;
}
