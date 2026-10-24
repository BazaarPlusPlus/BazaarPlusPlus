#nullable enable
using BazaarGameShared.Domain.Effect;
using BazaarGameShared.Domain.Effect.Trigger;
using BazaarPlusPlus.Core.Combat;
using BazaarPlusPlus.GameInterop.CombatSimulation;

namespace BazaarPlusPlus.Game.PostCombatImpact.Data;

internal static class CombatImpactCriticalTriggerReader
{
    internal static IReadOnlyDictionary<string, CombatEffectPriority>? Read(
        IEnumerable<TCardAbility>? abilities
    )
    {
        if (abilities == null)
            return null;

        var triggers = new Dictionary<string, CombatEffectPriority>(StringComparer.Ordinal);
        foreach (
            var group in abilities
                .Where(ability =>
                    ability != null
                    && ability.Trigger is TTriggerOnCardCritted
                    && !string.IsNullOrWhiteSpace(ability.Id)
                )
                .GroupBy(ability => ability.Id, StringComparer.Ordinal)
                .Where(group => group.Select(ability => ability.Priority).Distinct().Count() == 1)
        )
        {
            if (
                DesktopCombatFacets.EffectPriority.TryToFacet(
                    group.First().Priority,
                    out var priority
                )
            )
                triggers[group.Key] = priority;
        }
        return triggers.Count == 0 ? null : triggers;
    }
}
