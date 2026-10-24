using BazaarGameShared.Domain.Core.Types;
using BazaarGameShared.Domain.Effect;
using BazaarGameShared.Domain.Values;
using BazaarPlusPlus.Core.Combat;
using BazaarPlusPlus.GameInterop.CombatSimulation;
using Xunit;

namespace CombatImpactAdapter.Tests;

/// <summary>
/// Vocabulary pin (ADR-0019): every member of the combat enums the Managed snapshot defines maps
/// to a Core combat facet member of the same name and value, so a game update that adds, drops
/// or renumbers one fails here instead of silently dropping events from Combat Impact.
/// </summary>
public sealed class DesktopCombatFacetVocabularyTests
{
    public static TheoryData<string, string[]> Maps =>
        new()
        {
            { nameof(EActionCommandType), [.. DesktopCombatFacets.ActionType.UnmappedSourceNames] },
            {
                nameof(ECardAttributeType),
                [.. DesktopCombatFacets.CardAttribute.UnmappedSourceNames]
            },
            {
                nameof(EPlayerAttributeType),
                [.. DesktopCombatFacets.PlayerAttribute.UnmappedSourceNames]
            },
            { nameof(ECombatantId), [.. DesktopCombatFacets.CombatantId.UnmappedSourceNames] },
            { nameof(ECardStats), [.. DesktopCombatFacets.CardStat.UnmappedSourceNames] },
            {
                nameof(EPlayerHealthChangeType),
                [.. DesktopCombatFacets.HealthChange.UnmappedSourceNames]
            },
            {
                nameof(EAttributeModifierOperation),
                [.. DesktopCombatFacets.AttributeOperation.UnmappedSourceNames]
            },
            { nameof(EDamageType), [.. DesktopCombatFacets.DamageType.UnmappedSourceNames] },
            {
                nameof(EListComparisonOperator),
                [.. DesktopCombatFacets.ListComparison.UnmappedSourceNames]
            },
            {
                nameof(EEffectPriority),
                [.. DesktopCombatFacets.EffectPriority.UnmappedSourceNames]
            },
            {
                nameof(EValueModifierMode),
                [.. DesktopCombatFacets.ValueModifierMode.UnmappedSourceNames]
            },
        };

    public static TheoryData<Type, Type> Vocabularies =>
        new()
        {
            { typeof(EActionCommandType), typeof(CombatActionType) },
            { typeof(ECardAttributeType), typeof(CombatCardAttribute) },
            { typeof(EPlayerAttributeType), typeof(CombatPlayerAttribute) },
            { typeof(ECombatantId), typeof(Combatant) },
            { typeof(ECardStats), typeof(CombatCardStat) },
            { typeof(EPlayerHealthChangeType), typeof(CombatHealthChange) },
            { typeof(EAttributeModifierOperation), typeof(CombatAttributeOperation) },
            { typeof(EDamageType), typeof(CombatDamageType) },
            { typeof(EListComparisonOperator), typeof(CombatListComparison) },
            { typeof(EEffectPriority), typeof(CombatEffectPriority) },
            { typeof(EValueModifierMode), typeof(CombatValueModifierMode) },
        };

    [Theory]
    [MemberData(nameof(Maps))]
    public void Every_game_combat_enum_member_has_a_combat_facet(
        string gameEnum,
        string[] unmapped
    ) =>
        Assert.True(
            unmapped.Length == 0,
            $"{gameEnum} defines {string.Join(", ", unmapped)} with no combat facet member."
        );

    // The facets copy the game's values so sets and sorts order as the game's do; the projector's
    // output order, and with it the Combat Impact goldens, depends on that.
    [Theory]
    [MemberData(nameof(Vocabularies))]
    public void Combat_facets_mirror_the_game_enum_names_and_values(Type game, Type facet) =>
        Assert.Equal(Members(game), Members(facet));

    private static string[] Members(Type type) =>
        Enum.GetNames(type)
            .Select(name =>
                $"{name}={Convert.ToInt64(Enum.Parse(type, name), System.Globalization.CultureInfo.InvariantCulture)}"
            )
            .Order(StringComparer.Ordinal)
            .ToArray();
}
