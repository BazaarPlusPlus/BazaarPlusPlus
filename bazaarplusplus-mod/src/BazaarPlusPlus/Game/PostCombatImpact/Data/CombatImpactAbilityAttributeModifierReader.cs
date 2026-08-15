#nullable enable
using BazaarGameShared.Domain.Effect;
using BazaarGameShared.Domain.Effect.Actions;

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
}
