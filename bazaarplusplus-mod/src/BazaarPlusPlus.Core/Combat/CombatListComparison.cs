#nullable enable
namespace BazaarPlusPlus.Core.Combat;

/// <summary>
/// How a tag condition compares its tags with a card's.
/// A combat facet (ADR-0019): members mirror the game's enum names so host adapters map by
/// name. The values copy the game's only so set and sort order match the game's; nothing casts
/// between the two, and no value is persisted or sent.
/// </summary>
public enum CombatListComparison
{
    All = 0,
    Any = 1,
    None = 2,
}
