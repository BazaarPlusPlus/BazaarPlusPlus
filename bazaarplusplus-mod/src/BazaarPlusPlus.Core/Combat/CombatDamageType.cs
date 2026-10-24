#nullable enable
namespace BazaarPlusPlus.Core.Combat;

/// <summary>
/// The kind of a player health adjustment.
/// A combat facet (ADR-0019): members mirror the game's enum names so host adapters map by
/// name. The values copy the game's only so set and sort order match the game's; nothing casts
/// between the two, and no value is persisted or sent.
/// </summary>
public enum CombatDamageType
{
    Damage = 0,
    Burn = 1,
    Poison = 2,
    Sandstorm = 3,
    Heal = 4,
    Shield = 5,
    Joy = 6,
    Regen = 7,
}
