#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
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

        // When the choice screen offers a specific enchant pedestal, keep only the
        // type(s) it would actually apply, so the preview matches "this pedestal".
        // Empty/null restriction (manual Ctrl, Always mode, not on a pedestal) keeps
        // the full list — unchanged behavior.
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
