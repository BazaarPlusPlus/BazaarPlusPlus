#nullable enable
using System.Collections.Generic;
using BazaarGameShared.Domain.Cards;
using BazaarGameShared.Domain.Cards.Item;
using BazaarGameShared.Domain.Core.Types;

namespace BazaarPlusPlus.Game.CollectionPanel.Data;

internal static class CollectionDerivedKeywordFacts
{
    private static readonly ETier[] LifestealLookupTiers =
    {
        ETier.Bronze,
        ETier.Silver,
        ETier.Gold,
        ETier.Diamond,
        ETier.Legendary,
    };

    public static IReadOnlyCollection<EHiddenTag> ProjectHiddenTags(TCardBase template)
    {
        var hiddenTags = template.HiddenTags;
        if (hiddenTags.Contains(EHiddenTag.Lifesteal))
            return hiddenTags;

        if (template is not TCardItem item || !HasPositiveLifesteal(item))
            return hiddenTags;

        return new HashSet<EHiddenTag>(hiddenTags) { EHiddenTag.Lifesteal };
    }

    private static bool HasPositiveLifesteal(TCardItem item)
    {
        foreach (var tier in LifestealLookupTiers)
        {
            if (item.GetAttributeBaseValueAtTier(ECardAttributeType.Lifesteal, tier) > 0)
                return true;
        }

        return false;
    }
}
