#nullable enable
namespace BazaarPlusPlus.Core.Combat;

/// <summary>
/// The pool a player health adjustment changed.
/// A combat facet (ADR-0019): members mirror the game's enum names so host adapters map by
/// name. The values copy the game's only so set and sort order match the game's; nothing casts
/// between the two, and no value is persisted or sent.
/// </summary>
public enum CombatHealthChange
{
    Health = 0,
    Joy = 1,
    Shield = 2,
}
