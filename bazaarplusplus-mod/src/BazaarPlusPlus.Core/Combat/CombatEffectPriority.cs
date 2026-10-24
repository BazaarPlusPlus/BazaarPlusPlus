#nullable enable
namespace BazaarPlusPlus.Core.Combat;

/// <summary>
/// The priority an effect's trigger runs at.
/// A combat facet (ADR-0019): members mirror the game's enum names so host adapters map by
/// name. The values copy the game's only so set and sort order match the game's; nothing casts
/// between the two, and no value is persisted or sent.
/// </summary>
public enum CombatEffectPriority
{
    Lowest = 0,
    Low = 1,
    Medium = 2,
    High = 3,
    Highest = 4,
    Immediate = 5,
}
