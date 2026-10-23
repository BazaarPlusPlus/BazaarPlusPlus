#nullable enable
namespace BazaarPlusPlus.Core.Cards;

/// <summary>
/// The board socket a captured card's left edge sits in. Board order comes from
/// <see cref="CardFacetOrder.SocketRank"/>, never the integer.
/// A card facet (ADR-0015): members mirror the game's enum names so host adapters map by name;
/// the integer value is BPP's own and never persisted or sent.
/// </summary>
public enum CardSocket
{
    Socket_0,
    Socket_1,
    Socket_2,
    Socket_3,
    Socket_4,
    Socket_5,
    Socket_6,
    Socket_7,
    Socket_8,
    Socket_9,
}
