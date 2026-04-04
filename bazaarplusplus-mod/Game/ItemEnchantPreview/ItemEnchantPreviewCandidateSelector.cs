using System.Collections.Generic;
using System.Linq;
using BazaarGameShared.Domain.Core.Types;

namespace BazaarPlusPlus.Game.ItemEnchantPreview;

public static class ItemEnchantPreviewCandidateSelector
{
    public static IReadOnlyList<EEnchantmentType> SelectCandidates(
        EEnchantmentType? currentEnchantment,
        IEnumerable<EEnchantmentType> allEnchantments
    )
    {
        return (allEnchantments?.Distinct() ?? new List<EEnchantmentType>())
            .Where(enchantment => enchantment != currentEnchantment)
            .ToList();
    }
}
