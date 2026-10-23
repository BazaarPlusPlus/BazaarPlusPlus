#nullable enable
namespace BazaarPlusPlus.Core.Cards;

/// <summary>
/// A hero a card belongs to. <see cref="TheDragons"/> is the canonical id across the game's
/// enum-name transition (<see cref="BazaarPlusPlus.Core.GameState.TheDragonsHeroId"/>); host adapters fold the legacy name into it.
/// A card facet (ADR-0015): members mirror the game's enum names so host adapters map by name;
/// the integer value is BPP's own and never persisted or sent.
/// </summary>
public enum CardHero
{
    Common,
    Pygmalien,
    Vanessa,
    Stelle,
    Jules,
    Dooley,
    Mak,
    Karnok,
    TheDragons,
}
