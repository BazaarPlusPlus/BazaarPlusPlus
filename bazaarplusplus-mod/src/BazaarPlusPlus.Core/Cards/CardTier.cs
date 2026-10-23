#nullable enable
namespace BazaarPlusPlus.Core.Cards;

/// <summary>
/// A card's quality tier. Visible order comes from <see cref="CardFacetOrder.TierRank"/>, never the integer.
/// A card facet (ADR-0015): members mirror the game's enum names so host adapters map by name;
/// the integer value is BPP's own and never persisted or sent.
/// </summary>
public enum CardTier
{
    Bronze,
    Silver,
    Gold,
    Diamond,
    Legendary,
}
