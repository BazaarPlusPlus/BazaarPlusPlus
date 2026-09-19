#nullable enable
using BazaarGameShared.Domain.Effect;
using BazaarGameShared.Domain.Effect.Actions;
using BazaarGameShared.Domain.Effect.AuraActions;

namespace BazaarPlusPlus.Game.PostCombatImpact.Data;

internal static class CombatImpactAbilityAttributeModifierReader
{
    internal static IReadOnlyDictionary<string, TActionCardModifyAttribute>? Read(
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

        return modifiers.Count == 0 ? null : modifiers;
    }

    internal static IReadOnlyDictionary<string, TAuraActionCardModifyAttribute>? ReadAuras(
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
        return modifiers.Count == 0 ? null : modifiers;
    }
}
