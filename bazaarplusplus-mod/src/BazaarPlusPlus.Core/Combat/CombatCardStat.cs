#nullable enable
namespace BazaarPlusPlus.Core.Combat;

/// <summary>
/// A per-card combat total the simulation reports.
/// A combat facet (ADR-0019): members mirror the game's enum names so host adapters map by
/// name. The values copy the game's only so set and sort order match the game's; nothing casts
/// between the two, and no value is persisted or sent.
/// </summary>
public enum CombatCardStat
{
    DamageDone = 0,
    ShieldAdded = 1,
    HealAdded = 2,
    JoyAdded = 3,
    PoisonAdded = 4,
    BurnAdded = 5,
    HastedCardsCount = 6,
    SlowedCardsCount = 7,
    FrozenCardsCount = 8,
    UseCount = 9,
    RegenAdded = 10,
    RageAdded = 11,
    TempoAdded = 12,
    TempoSpent = 13,
}
