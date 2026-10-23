#nullable enable
namespace BazaarPlusPlus.Core.Cards;

/// <summary>
/// A card template's kind.
/// A card facet (ADR-0015): members mirror the game's enum names so host adapters map by name;
/// the integer value is BPP's own and never persisted or sent.
/// </summary>
public enum CardType
{
    Item,
    Skill,
    CombatEncounter,
    EncounterStep,
    EventEncounter,
    PedestalEncounter,
    PvpEncounter,
    SocketEffect,
    PlayerEffect,
}
