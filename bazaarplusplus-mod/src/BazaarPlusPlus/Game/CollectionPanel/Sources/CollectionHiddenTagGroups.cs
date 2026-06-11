#nullable enable
using System;
using System.Collections.Generic;
using BazaarGameShared.Domain.Core.Types;

namespace BazaarPlusPlus.Game.CollectionPanel.Sources;

internal static class CollectionHiddenTagGroups
{
    private static readonly IReadOnlyDictionary<string, IReadOnlyList<EHiddenTag>> Groups =
        new Dictionary<string, IReadOnlyList<EHiddenTag>>(StringComparer.OrdinalIgnoreCase)
        {
            ["Ammo"] = new[] { EHiddenTag.Ammo, EHiddenTag.AmmoReference },
            ["Burn"] = new[] { EHiddenTag.Burn, EHiddenTag.BurnReference },
            ["Cooldown"] = new[] { EHiddenTag.Cooldown, EHiddenTag.CooldownReference },
            ["Crit"] = new[] { EHiddenTag.Crit, EHiddenTag.CritReference },
            ["Flying"] = new[] { EHiddenTag.Flying, EHiddenTag.FlyingReference },
            ["Freeze"] = new[] { EHiddenTag.Freeze, EHiddenTag.FreezeReference },
            ["Haste"] = new[] { EHiddenTag.Haste, EHiddenTag.HasteReference },
            ["Health"] = new[] { EHiddenTag.Health, EHiddenTag.HealthReference },
            ["Heal"] = new[] { EHiddenTag.Heal, EHiddenTag.HealReference },
            ["Poison"] = new[] { EHiddenTag.Poison, EHiddenTag.PoisonReference },
            ["Regen"] = new[] { EHiddenTag.Regen, EHiddenTag.RegenReference },
            ["Shield"] = new[] { EHiddenTag.Shield, EHiddenTag.ShieldReference },
            ["Slow"] = new[] { EHiddenTag.Slow, EHiddenTag.SlowReference },
        };

    public static IReadOnlyList<EHiddenTag> Expand(IReadOnlyList<string>? groupNames, string path)
    {
        if (groupNames == null || groupNames.Count == 0)
            return Array.Empty<EHiddenTag>();

        var result = new List<EHiddenTag>();
        var used = new HashSet<EHiddenTag>();
        foreach (var raw in groupNames)
        {
            var groupName = raw?.Trim();
            if (string.IsNullOrWhiteSpace(groupName))
                throw new InvalidOperationException($"{path} contains an empty hidden tag group.");
            if (!Groups.TryGetValue(groupName!, out var tags))
                throw new InvalidOperationException(
                    $"{path} has unknown hidden tag group '{groupName}'."
                );

            foreach (var tag in tags)
            {
                if (used.Add(tag))
                    result.Add(tag);
            }
        }
        return result;
    }
}
