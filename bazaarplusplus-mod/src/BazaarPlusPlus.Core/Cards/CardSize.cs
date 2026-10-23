#nullable enable
namespace BazaarPlusPlus.Core.Cards;

/// <summary>
/// An item card's size. Visible order comes from <see cref="CardFacetOrder.SizeRank"/>, never the integer.
/// A card facet (ADR-0015): members mirror the game's enum names so host adapters map by name;
/// the integer value is BPP's own and never persisted or sent.
/// </summary>
public enum CardSize
{
    Small,
    Medium,
    Large,
}
