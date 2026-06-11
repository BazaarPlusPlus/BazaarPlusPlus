#nullable enable
using System;
using System.Collections.Generic;
using BazaarGameShared.Domain.Core.Types;

namespace BazaarPlusPlus.Game.CollectionPanel.Data;

// Pure function: (catalog + filter state) -> ordered visible list.
//
// Ordering is deliberately explicit: selected priority first, then the other card facet, then
// display name.
// The rank helpers keep visible ordering independent from raw enum integer values.
internal static class CollectionFilterEngine
{
    public static List<CollectionCardVm> Apply(
        IReadOnlyList<CollectionCardVm> all,
        CollectionFilterState filter,
        CollectionFilterContext? context = null
    )
    {
        context ??= new CollectionFilterContext();
        var result = new List<CollectionCardVm>(all.Count);
        var offerPoolSet =
            context.OfferedCardIds == null
                ? null
                : context.OfferedCardIds as HashSet<Guid>
                    ?? new HashSet<Guid>(context.OfferedCardIds);
        var profile = CollectionTabProfile.For(filter.ActiveType);
        var heroFilterCount = context.ApplyHeroFilter ? filter.Heroes.Count : 0;
        var tierFilterCount = filter.Tiers.Count;
        var tagFilterCount = profile.ShowTagFilter ? filter.Tags.Count : 0;
        var keywordFilterCount = profile.ShowKeywordFilter ? filter.Keywords.Count : 0;
        var sizeFilterCount = profile.ShowSizeFilter ? filter.Sizes.Count : 0;
        // In-run only; null disables. Independent of the manual Tier row — both narrow by tier.
        // Fixed-tier sources are exempt: their pool ignores the day's tier ceiling.
        var dayFilter = context.SuppressDayGate ? null : filter.SelectedRunDay;

        foreach (var card in all)
        {
            if (card.Type != filter.ActiveType)
                continue;
            if (filter.PackagesOnly)
            {
                if (card.IsPackage)
                    result.Add(card);
                continue;
            }
            if (card.IsPackage)
                continue;
            if (offerPoolSet != null && !offerPoolSet.Contains(card.Id))
                continue;
            if (heroFilterCount > 0 && !CollectionHeroScope.MatchesFilter(card, filter))
                continue;
            if (tierFilterCount > 0 && !filter.Tiers.Contains(card.StartingTier))
                continue;
            if (dayFilter is int day && !DayTierSchedule.AllowsStartingTier(card.StartingTier, day))
                continue;
            if (tagFilterCount > 0 && !MatchesFacet(card.Tags, filter.Tags, filter.TagMatchMode))
                continue;
            if (
                keywordFilterCount > 0
                && !MatchesFacet(card.HiddenTags, filter.Keywords, filter.KeywordMatchMode)
            )
                continue;
            if (sizeFilterCount > 0 && !filter.Sizes.Contains(card.Size))
                continue;
            result.Add(card);
        }

        result.Sort(
            (a, b) =>
            {
                var facetOrder =
                    filter.SortPriority == CollectionSortPriority.Size
                        ? CompareBySizeThenTier(a, b)
                        : CompareByTierThenSize(a, b);
                if (facetOrder != 0)
                    return facetOrder;
                return string.Compare(
                    a.DisplayName,
                    b.DisplayName,
                    StringComparison.CurrentCultureIgnoreCase
                );
            }
        );
        return result;
    }

    private static int CompareByTierThenSize(CollectionCardVm a, CollectionCardVm b)
    {
        var tierOrder = TierRank(a.StartingTier).CompareTo(TierRank(b.StartingTier));
        if (tierOrder != 0)
            return tierOrder;
        return SizeRank(a.Size).CompareTo(SizeRank(b.Size));
    }

    private static int CompareBySizeThenTier(CollectionCardVm a, CollectionCardVm b)
    {
        var sizeOrder = SizeRank(a.Size).CompareTo(SizeRank(b.Size));
        if (sizeOrder != 0)
            return sizeOrder;
        return TierRank(a.StartingTier).CompareTo(TierRank(b.StartingTier));
    }

    private static bool MatchesFacet<T>(
        IReadOnlyCollection<T> cardValues,
        HashSet<T> filterValues,
        CollectionFacetMatchMode mode
    ) =>
        mode == CollectionFacetMatchMode.All
            ? AllSelectedValuesMatch(cardValues, filterValues)
            : AnySelectedValueMatches(cardValues, filterValues);

    private static bool AnySelectedValueMatches<T>(
        IReadOnlyCollection<T> cardValues,
        HashSet<T> filterValues
    )
    {
        foreach (var value in cardValues)
        {
            if (filterValues.Contains(value))
                return true;
        }
        return false;
    }

    private static bool AllSelectedValuesMatch<T>(
        IReadOnlyCollection<T> cardValues,
        HashSet<T> filterValues
    )
    {
        if (cardValues.Count < filterValues.Count)
            return false;

        var cardValueSet = cardValues as HashSet<T> ?? new HashSet<T>(cardValues);
        foreach (var selected in filterValues)
        {
            if (!cardValueSet.Contains(selected))
                return false;
        }
        return true;
    }

    private static int TierRank(ETier tier) => CollectionCardFacetRanks.TierRank(tier);

    private static int SizeRank(ECardSize size) => CollectionCardFacetRanks.SizeRank(size);
}
