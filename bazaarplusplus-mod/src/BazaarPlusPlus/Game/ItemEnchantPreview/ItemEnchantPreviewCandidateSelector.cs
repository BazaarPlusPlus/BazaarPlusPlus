#nullable enable
using BazaarGameShared.Domain.Core.Types;

namespace BazaarPlusPlus.Game.ItemEnchantPreview;

public static class ItemEnchantPreviewCandidateSelector
{
    public static IReadOnlyList<EEnchantmentType> SelectCandidates(
        EEnchantmentType? currentEnchantment,
        IEnumerable<EEnchantmentType> allEnchantments,
        IReadOnlyCollection<string>? restrictToEnchantmentNames = null
    )
    {
        var candidates = (allEnchantments?.Distinct() ?? new List<EEnchantmentType>()).Where(
            enchantment => enchantment != currentEnchantment
        );

        if (restrictToEnchantmentNames != null && restrictToEnchantmentNames.Count > 0)
        {
            var allowed = new HashSet<string>(
                restrictToEnchantmentNames,
                StringComparer.OrdinalIgnoreCase
            );
            candidates = candidates.Where(enchantment => allowed.Contains(enchantment.ToString()));
        }

        return candidates.ToList();
    }
}
