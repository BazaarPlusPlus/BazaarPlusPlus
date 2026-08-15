#nullable enable
using BazaarGameClient.Domain.Models.Cards;
using BazaarGameShared.Domain.Effect;

namespace BazaarPlusPlus.Game.PostCombatImpact.Data;

internal static class CombatImpactActiveAbilityReader
{
    internal static IReadOnlyList<TCardAbility> Read(
        ItemCard? item,
        IEnumerable<TCardAbility>? tierAbilities
    )
    {
        try
        {
            var abilities = tierAbilities?.ToList() ?? [];
            if (
                item?.Enchantment is { } enchantmentType
                && item.GetEnchantments() is { } enchantments
                && enchantments.TryGetValue(enchantmentType, out var enchantment)
            )
                abilities.AddRange(enchantment.Abilities.Values);

            return abilities;
        }
        catch
        {
            return tierAbilities?.ToArray() ?? [];
        }
    }
}
