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

    public static readonly CardFacetNameMap<EInventorySection, CardSection> Section = new();

    public static readonly CardFacetNameMap<EContainerSocketId, CardSocket> Socket = new();

    public static bool IsHeroAvailable(CardHero hero) => Hero.TryToSource(hero, out _);

    // A captured card's facets. A type the facets lack fails the capture, so the board degrades to
    // Missing instead of carrying a wrong card; a size, section or socket they lack reads as none.
    // The game's size 0, which names no member, is a card without a size.
    public static CardType CapturedType(ECardType type) =>
        Type.TryToFacet(type, out var facet)
            ? facet
            : throw new InvalidDataException($"ECardType '{type}' has no card facet.");

    public static CardSize? CapturedSize(ECardSize? size) =>
        size is { } value && Size.TryToFacet(value, out var facet) ? facet : null;

    public static CardSection? CapturedSection(EInventorySection? section) =>
        section is { } value && Section.TryToFacet(value, out var facet) ? facet : null;

    public static CardSocket? CapturedSocket(EContainerSocketId? socket) =>
        socket is { } value && Socket.TryToFacet(value, out var facet) ? facet : null;

    public static CardHero? CapturedHero(EHero? hero) =>
        hero is { } value && Hero.TryToFacet(value, out var facet) ? facet : null;

    public static CardEnchantment? CapturedEnchantment(EEnchantmentType? enchantment) =>
        enchantment is { } value && Enchantment.TryToFacet(value, out var facet) ? facet : null;

    // Bronze, the game's own default tier, for a tier the facets lack.
    public static CardTier CapturedTier(ETier tier) =>
        Tier.TryToFacet(tier, out var facet) ? facet : CardTier.Bronze;

    // The game values a captured card's facets name. A card without a size gets the game's 0,
    // which is what the game itself reported for it.
    public static ECardType GameType(CardType type) =>
        Type.TryToSource(type, out var source) ? source : default;

    public static ECardSize GameSize(CardSize? size) =>
        size is { } value && Size.TryToSource(value, out var source) ? source : default;

    public static EInventorySection? GameSection(CardSection? section) =>
        section is { } value && Section.TryToSource(value, out var source) ? source : null;

    public static ETier GameTier(CardTier tier) =>
        Tier.TryToSource(tier, out var source) ? source : ETier.Bronze;

    public static EEnchantmentType? GameEnchantment(CardEnchantment? enchantment) =>
        enchantment is { } value && Enchantment.TryToSource(value, out var source) ? source : null;

    public static EContainerSocketId? GameSocket(CardSocket? socket) =>
        socket is { } value && Socket.TryToSource(value, out var source) ? source : null;

    public static string TheDragonsDisplayName() =>
        Hero.TryToSource(CardHero.TheDragons, out var hero)
            ? TheDragonsHeroIdentity.ResolveDisplayName(hero)
            : TheDragonsHeroIdentity.FallbackDisplayName;
}
