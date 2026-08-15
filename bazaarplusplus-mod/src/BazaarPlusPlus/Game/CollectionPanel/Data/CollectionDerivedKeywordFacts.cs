#nullable enable
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
        var item = template as TCardItem;
        var hasQuest = item?.Quests?.Count > 0;
        var needsLifesteal =
            !hiddenTags.Contains(EHiddenTag.Lifesteal)
            && item != null
            && HasPositiveLifesteal(item);

        if (!hasQuest && !needsLifesteal)
            return hiddenTags;

        var projected = new HashSet<EHiddenTag>(hiddenTags);
        if (hasQuest)
            projected.Add(EHiddenTag.Quest);
        if (needsLifesteal)
            projected.Add(EHiddenTag.Lifesteal);
        return projected;
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
