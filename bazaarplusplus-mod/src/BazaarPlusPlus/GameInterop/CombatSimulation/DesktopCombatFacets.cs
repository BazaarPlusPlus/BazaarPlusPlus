#nullable enable
using BazaarGameShared.Domain.Core.Types;
using BazaarGameShared.Domain.Effect;
using BazaarGameShared.Domain.Values;
using BazaarPlusPlus.Core.Cards;
using BazaarPlusPlus.Core.Combat;

namespace BazaarPlusPlus.GameInterop.CombatSimulation;

/// <summary>
/// The desktop host's combat facet adapter (ADR-0019): the Mono game's combat enums mapped to
/// Core's combat vocabulary by member name. The Android host has the same adapter over the
/// interop enums; a pin test on each host fails when the game adds a member Core lacks.
/// </summary>
internal static class DesktopCombatFacets
{
    public static readonly CardFacetNameMap<EActionCommandType, CombatActionType> ActionType =
        new();

    public static readonly CardFacetNameMap<ECardAttributeType, CombatCardAttribute> CardAttribute =
        new();

    public static readonly CardFacetNameMap<
        EPlayerAttributeType,
        CombatPlayerAttribute
    > PlayerAttribute = new();

    public static readonly CardFacetNameMap<ECombatantId, Combatant> CombatantId = new();

    public static readonly CardFacetNameMap<ECardStats, CombatCardStat> CardStat = new();

    public static readonly CardFacetNameMap<
        EPlayerHealthChangeType,
        CombatHealthChange
    > HealthChange = new();

    public static readonly CardFacetNameMap<
        EAttributeModifierOperation,
        CombatAttributeOperation
    > AttributeOperation = new();

    public static readonly CardFacetNameMap<EDamageType, CombatDamageType> DamageType = new();

    public static readonly CardFacetNameMap<
        EListComparisonOperator,
        CombatListComparison
    > ListComparison = new();

    public static readonly CardFacetNameMap<EEffectPriority, CombatEffectPriority> EffectPriority =
        new();

    public static readonly CardFacetNameMap<
        EValueModifierMode,
        CombatValueModifierMode
    > ValueModifierMode = new();

    /// <summary>The game's card attribute map in its own order, without the attributes Core lacks.</summary>
    public static Dictionary<CombatCardAttribute, int> CardAttributes(
        IEnumerable<KeyValuePair<ECardAttributeType, int>> attributes
    )
    {
        var result = new Dictionary<CombatCardAttribute, int>();
        foreach (var (attribute, value) in attributes)
        {
            if (CardAttribute.TryToFacet(attribute, out var facet))
                result[facet] = value;
        }
        return result;
    }

    /// <summary>The game's attribute map for Core's, as a native card preview takes it.</summary>
    public static IReadOnlyDictionary<ECardAttributeType, int>? GameCardAttributes(
        IReadOnlyDictionary<CombatCardAttribute, int>? attributes
    )
    {
        if (attributes == null)
            return null;
        var result = new Dictionary<ECardAttributeType, int>();
        foreach (var (attribute, value) in attributes)
        {
            if (CardAttribute.TryToSource(attribute, out var source))
                result[source] = value;
        }
        return result;
    }

    /// <summary>An effect-id-to-attribute map in the game's order; null when nothing maps.</summary>
    public static IReadOnlyDictionary<string, CombatCardAttribute>? AttributesByEffectId(
        IEnumerable<KeyValuePair<string, ECardAttributeType>>? attributes
    )
    {
        if (attributes == null)
            return null;
        var result = new Dictionary<string, CombatCardAttribute>(StringComparer.Ordinal);
        foreach (var (effectId, attribute) in attributes)
        {
            if (CardAttribute.TryToFacet(attribute, out var facet))
                result[effectId] = facet;
        }
        return result.Count == 0 ? null : result;
    }

    public static Combatant? ToCombatant(ECombatantId? combatant) =>
        combatant is { } value && CombatantId.TryToFacet(value, out var facet) ? facet : null;
}
