#nullable enable
namespace BazaarPlusPlus.Core.Combat;

/// <summary>
/// How a value modifier combines its operand with the value.
/// A combat facet (ADR-0019): members mirror the game's enum names so host adapters map by
/// name. The values copy the game's only so set and sort order match the game's; nothing casts
/// between the two, and no value is persisted or sent.
/// </summary>
public enum CombatValueModifierMode
{
    Add = 0,
    Subtract = 1,
    Multiply = 2,
    Divide = 3,
}
