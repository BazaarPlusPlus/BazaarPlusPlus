#nullable enable
using BazaarGameShared.Domain.Core.Types;
using BazaarGameShared.Domain.Effect;
using BazaarGameShared.Domain.Effect.Trigger;

namespace BazaarPlusPlus.Game.PostCombatImpact.Data;

internal static class CombatImpactCriticalTriggerReader
{
    internal static IReadOnlyDictionary<string, EEffectPriority>? Read(
        IEnumerable<TCardAbility>? abilities
    )
    {
        if (abilities == null)
            return null;

        var triggers = abilities
            .Where(ability =>
                ability != null
                && ability.Trigger is TTriggerOnCardCritted
                && !string.IsNullOrWhiteSpace(ability.Id)
            )
            .GroupBy(ability => ability.Id, StringComparer.Ordinal)
            .Where(group => group.Select(ability => ability.Priority).Distinct().Count() == 1)
            .ToDictionary(
                group => group.Key,
                group => group.First().Priority,
                StringComparer.Ordinal
            );
        return triggers.Count == 0 ? null : triggers;
    }
}
