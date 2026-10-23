#nullable enable
using BazaarPlusPlus.Core.Cards;

namespace BazaarPlusPlus.Game.CollectionPanel.Sources;

public static class CollectionHiddenTagGroups
{
    private static readonly IReadOnlyDictionary<string, IReadOnlyList<CardHiddenTag>> Groups =
        new Dictionary<string, IReadOnlyList<CardHiddenTag>>(StringComparer.OrdinalIgnoreCase)
        {
            ["Ammo"] = new[] { CardHiddenTag.Ammo, CardHiddenTag.AmmoReference },
            ["Burn"] = new[] { CardHiddenTag.Burn, CardHiddenTag.BurnReference },
            ["Cooldown"] = new[] { CardHiddenTag.Cooldown, CardHiddenTag.CooldownReference },
            ["Crit"] = new[] { CardHiddenTag.Crit, CardHiddenTag.CritReference },
            ["Flying"] = new[] { CardHiddenTag.Flying, CardHiddenTag.FlyingReference },
            ["Freeze"] = new[] { CardHiddenTag.Freeze, CardHiddenTag.FreezeReference },
            ["Haste"] = new[] { CardHiddenTag.Haste, CardHiddenTag.HasteReference },
            ["Health"] = new[] { CardHiddenTag.Health, CardHiddenTag.HealthReference },
            ["Heal"] = new[] { CardHiddenTag.Heal, CardHiddenTag.HealReference },
            ["Poison"] = new[] { CardHiddenTag.Poison, CardHiddenTag.PoisonReference },
            ["Regen"] = new[] { CardHiddenTag.Regen, CardHiddenTag.RegenReference },
            ["Shield"] = new[] { CardHiddenTag.Shield, CardHiddenTag.ShieldReference },
            ["Slow"] = new[] { CardHiddenTag.Slow, CardHiddenTag.SlowReference },
        };

    public static IReadOnlyList<CardHiddenTag> Expand(
        IReadOnlyList<string>? groupNames,
        string path
    )
    {
        if (groupNames == null || groupNames.Count == 0)
            return Array.Empty<CardHiddenTag>();

        var result = new List<CardHiddenTag>();
        var used = new HashSet<CardHiddenTag>();
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
