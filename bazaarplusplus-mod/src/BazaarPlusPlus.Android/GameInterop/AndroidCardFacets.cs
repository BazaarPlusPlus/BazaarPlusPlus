using BazaarPlusPlus.Core.Cards;
using BazaarPlusPlus.Core.GameState;
using Il2CppBazaarGameShared.Domain.Core.Types;

namespace BazaarPlusPlus.Android.GameInterop;

/// <summary>
/// The Android host's card facet adapter (ADR-0015): the interop enums, managed copies of the
/// game's with the same names, mapped to BPP card facets by member name. Mirrors the desktop
/// host's <c>DesktopCardFacets</c>.
/// </summary>
internal static class AndroidCardFacets
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

    public static readonly CardFacetNameMap<EInventorySection, CardSection> Section = new();

    public static readonly CardFacetNameMap<EContainerSocketId, CardSocket> Socket = new();

    public static bool IsHeroAvailable(CardHero hero) => Hero.TryToSource(hero, out _);

    // The game values a replay restores a captured card with, as the desktop's DesktopCardFacets.
    public static ECardType GameType(CardType type) =>
        Type.TryToSource(type, out var source) ? source : default;

    public static ECardSize GameSize(CardSize? size) =>
        size is { } value && Size.TryToSource(value, out var source) ? source : default;

    public static EInventorySection? GameSection(CardSection? section) =>
        section is { } value && Section.TryToSource(value, out var source) ? source : null;

    public static EContainerSocketId? GameSocket(CardSocket? socket) =>
        socket is { } value && Socket.TryToSource(value, out var source) ? source : null;
}
