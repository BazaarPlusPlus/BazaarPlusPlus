#nullable enable
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Core.Cards;
using BazaarPlusPlus.Core.GameState;
using BazaarPlusPlus.GameInterop.Heroes;

namespace BazaarPlusPlus.GameInterop.Cards;

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

    // An unknown card type must fail capture to avoid storing a wrong card. The unnamed game
    // size 0 denotes a card without a size.
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

    public static CardTier CapturedTier(ETier tier) =>
        Tier.TryToFacet(tier, out var facet) ? facet : CardTier.Bronze;

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
