#nullable enable
namespace BazaarPlusPlus.Core.Cards;

/// <summary>
/// A public card tag (the game's types such as Weapon or Food).
/// A card facet (ADR-0015): members mirror the game's enum names so host adapters map by name;
/// the integer value is BPP's own and never persisted or sent.
/// </summary>
public enum CardTag
{
    Weapon,
    Property,
    Food,
    Potion,
    Tool,
    Vehicle,
    Aquatic,
    Friend,
    Core,
    Unsellable,
    Unstashable,
    Merchant,
    Event,
    Combat,
    Loot,
    Ray,
    Dinosaur,
    Apparel,
    Toy,
    Tech,
    Dragon,
    Ingredient,
    Relic,
    Reagent,
    Map,
    Key,
    Drone,
    Sigil,
    Trap,
    Instrument,
}
