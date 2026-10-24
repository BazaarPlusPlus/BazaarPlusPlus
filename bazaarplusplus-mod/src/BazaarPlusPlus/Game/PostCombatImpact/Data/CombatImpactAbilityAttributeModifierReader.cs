#nullable enable
using BazaarGameShared.Domain.Effect;
using BazaarGameShared.Domain.Effect.Actions;
using BazaarGameShared.Domain.Effect.AuraActions;
using BazaarPlusPlus.Core.Combat;
using BazaarPlusPlus.GameInterop.CombatSimulation;

namespace BazaarPlusPlus.Game.PostCombatImpact.Data;

// An effect id whose direct modifiers differ is ambiguous and left out. The comparison runs on the
// game's records, before the modifiers cross into Core's vocabulary.
internal static class CombatImpactAbilityAttributeModifierReader
{
    internal static IReadOnlyDictionary<string, CombatAttributeModifier>? Read(
        IEnumerable<TCardAbility>? abilities
    )
    {
        if (abilities == null)
            return null;

        var modifiers = new Dictionary<string, TActionCardModifyAttribute>(StringComparer.Ordinal);
        var ambiguousEffectIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var ability in abilities)
        {
            if (
                ability?.Action is not TActionCardModifyAttribute modifier
                || string.IsNullOrWhiteSpace(ability.Id)
                || ambiguousEffectIds.Contains(ability.Id)
            )
                continue;

            if (!modifiers.TryGetValue(ability.Id, out var existing))
            {
                modifiers[ability.Id] = modifier;
                continue;
            }
            if (existing == modifier)
                continue;

            modifiers.Remove(ability.Id);
            ambiguousEffectIds.Add(ability.Id);
        }

        return ToCore(modifiers, DesktopCombatTimeline.ToModifier);
    }

    internal static IReadOnlyDictionary<string, CombatAttributeModifier>? ReadAuras(
        IEnumerable<TCardAura>? auras
    )
    {
        if (auras == null)
            return null;
        var modifiers = new Dictionary<string, TAuraActionCardModifyAttribute>(
            StringComparer.Ordinal
        );
        var ambiguous = new HashSet<string>(StringComparer.Ordinal);
        foreach (var aura in auras)
        {
            if (
                aura?.Action is not TAuraActionCardModifyAttribute modifier
                || string.IsNullOrWhiteSpace(aura.Id)
                || ambiguous.Contains(aura.Id)
            )
                continue;
            if (!modifiers.TryGetValue(aura.Id, out var existing))
                modifiers.Add(aura.Id, modifier);
            else if (existing != modifier)
            {
                modifiers.Remove(aura.Id);
                ambiguous.Add(aura.Id);
            }
        }
        return ToCore(modifiers, DesktopCombatTimeline.ToModifier);
    }

    private static IReadOnlyDictionary<string, CombatAttributeModifier>? ToCore<TModifier>(
        Dictionary<string, TModifier> modifiers,
        Func<TModifier, CombatAttributeModifier?> convert
    )
    {
        var result = new Dictionary<string, CombatAttributeModifier>(StringComparer.Ordinal);
        foreach (var (effectId, modifier) in modifiers)
        {
            if (convert(modifier) is { } converted)
                result[effectId] = converted;
        }
        return result.Count == 0 ? null : result;
    }
}
