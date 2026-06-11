#nullable enable
using System;
using System.Collections.Generic;
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Game.CollectionPanel.Data;

namespace BazaarPlusPlus.Game.CollectionPanel.Sources;

internal static class CollectionSourceOfferPoolResolver
{
    public static CollectionSourceOfferPoolResult Resolve(
        CollectionSourceEntry? source,
        EHero? selectedHero,
        IReadOnlyList<CollectionCardVm> catalogCards
    )
    {
        if (catalogCards == null)
            throw new ArgumentNullException(nameof(catalogCards));
        if (source == null)
            return CollectionSourceOfferPoolResult.NoneSelected();

        var offeredCardIds = new HashSet<Guid>();
        var matchesByCardId = new Dictionary<Guid, IReadOnlyList<CollectionSourceOfferMatch>>();
        foreach (var card in catalogCards)
        {
            var matches = ResolveMatches(source, selectedHero, card);
            if (matches.Count == 0)
                continue;

            offeredCardIds.Add(card.Id);
            matchesByCardId[card.Id] = matches;
        }
        return CollectionSourceOfferPoolResult.Ready(offeredCardIds, matchesByCardId);
    }

    private static IReadOnlyList<CollectionSourceOfferMatch> ResolveMatches(
        CollectionSourceEntry source,
        EHero? selectedHero,
        CollectionCardVm card
    )
    {
        if (card.Type != CardTypeFor(source.Kind))
            return Array.Empty<CollectionSourceOfferMatch>();

        var matches = new List<CollectionSourceOfferMatch>();
        foreach (var segment in source.OfferSegments)
        {
            AddSegmentMatches(matches, source.Kind, segment, selectedHero, card);
        }
        return matches;
    }

    private static void AddSegmentMatches(
        List<CollectionSourceOfferMatch> matches,
        CollectionSourceKind sourceKind,
        CollectionSourceOfferSegment segment,
        EHero? selectedHero,
        CollectionCardVm card
    )
    {
        var rule = segment.Rule;
        if (!MatchesBaseRule(sourceKind, rule, selectedHero, card))
            return;

        if (segment.Kind == CollectionSourceOfferSegmentKind.Enchanted)
        {
            foreach (var enchantment in card.Enchantments.Values)
            {
                if (!MatchesEnchantment(rule, enchantment))
                    continue;
                matches.Add(
                    new CollectionSourceOfferMatch(
                        segment.Key,
                        segment.Kind,
                        segment.RarityLabel,
                        enchantment.Type
                    )
                );
            }
            return;
        }

        if (HasEnchantmentConstraints(rule) && !MatchesAnyEnchantment(rule, card))
            return;

        matches.Add(
            new CollectionSourceOfferMatch(segment.Key, segment.Kind, segment.RarityLabel, null)
        );
    }

    private static bool MatchesBaseRule(
        CollectionSourceKind sourceKind,
        CollectionSourceOfferRule rule,
        EHero? selectedHero,
        CollectionCardVm card
    )
    {
        if (!MatchesHero(sourceKind, rule, selectedHero, card.Heroes))
            return false;
        if (rule.StartingTier != null && !MatchesStartingTier(rule.StartingTier, card.StartingTier))
            return false;
        if (rule.SizesAny.Count > 0 && !Contains(rule.SizesAny, card.Size))
            return false;
        if (rule.TagsNone.Count > 0 && Overlaps(card.Tags, rule.TagsNone))
            return false;
        if (rule.TagsAny.Count > 0 && !Overlaps(card.Tags, rule.TagsAny))
            return false;
        if (rule.HiddenTagsAny.Count > 0 && !Overlaps(card.HiddenTags, rule.HiddenTagsAny))
            return false;
        if (rule.EnchantableOnly && !card.IsEnchantable)
            return false;
        return true;
    }

    private static bool MatchesAnyEnchantment(CollectionSourceOfferRule rule, CollectionCardVm card)
    {
        foreach (var enchantment in card.Enchantments.Values)
        {
            if (MatchesEnchantment(rule, enchantment))
                return true;
        }
        return false;
    }

    private static bool MatchesEnchantment(
        CollectionSourceOfferRule rule,
        CollectionCardEnchantmentFacets enchantment
    )
    {
        if (
            rule.EnchantmentTypesAny.Count > 0
            && !Contains(rule.EnchantmentTypesAny, enchantment.Type)
        )
            return false;
        if (
            rule.EnchantmentTagsAny.Count > 0
            && !Overlaps(enchantment.Tags, rule.EnchantmentTagsAny)
        )
            return false;
        if (
            rule.EnchantmentHiddenTagsAny.Count > 0
            && !Overlaps(enchantment.HiddenTags, rule.EnchantmentHiddenTagsAny)
        )
            return false;
        return HasEnchantmentConstraints(rule) || rule.EnchantableOnly;
    }

    private static bool HasEnchantmentConstraints(CollectionSourceOfferRule rule) =>
        rule.EnchantmentTypesAny.Count > 0
        || rule.EnchantmentTagsAny.Count > 0
        || rule.EnchantmentHiddenTagsAny.Count > 0;

    private static ECardType CardTypeFor(CollectionSourceKind kind) =>
        kind == CollectionSourceKind.Trainer ? ECardType.Skill : ECardType.Item;

    private static bool MatchesHero(
        CollectionSourceKind sourceKind,
        CollectionSourceOfferRule rule,
        EHero? selectedHero,
        IReadOnlyCollection<EHero> cardHeroes
    )
    {
        switch (rule.HeroMode)
        {
            case CollectionSourceHeroMode.AllHeroes:
                return true;

            case CollectionSourceHeroMode.FixedHero:
                if (!rule.Hero.HasValue)
                    return false;
                return sourceKind == CollectionSourceKind.Trainer
                    ? MatchesExclusiveHero(cardHeroes, rule.Hero.Value)
                    : Contains(cardHeroes, rule.Hero.Value);

            case CollectionSourceHeroMode.NeutralOnly:
                return Contains(cardHeroes, EHero.Common);

            case CollectionSourceHeroMode.SelectedHero:
                if (!selectedHero.HasValue)
                    return true;
                return Contains(cardHeroes, selectedHero.Value);

            default:
                return false;
        }
    }

    private static bool MatchesExclusiveHero(IReadOnlyCollection<EHero> cardHeroes, EHero hero) =>
        cardHeroes.Count == 1 && Contains(cardHeroes, hero);

    private static bool MatchesStartingTier(
        CollectionSourceStartingTierRule rule,
        ETier candidateTier
    )
    {
        if (rule.Mode == CollectionSourceStartingTierMode.Exact)
            return candidateTier == rule.Tier;

        return CollectionCardFacetRanks.TierRank(candidateTier)
            <= CollectionCardFacetRanks.TierRank(rule.Tier);
    }

    private static bool Contains<T>(IReadOnlyCollection<T> values, T target)
    {
        var comparer = EqualityComparer<T>.Default;
        foreach (var value in values)
        {
            if (comparer.Equals(value, target))
                return true;
        }
        return false;
    }

    private static bool Overlaps<T>(IReadOnlyCollection<T> left, IReadOnlyCollection<T> right)
    {
        var comparer = EqualityComparer<T>.Default;
        foreach (var leftValue in left)
        {
            foreach (var rightValue in right)
            {
                if (comparer.Equals(leftValue, rightValue))
                    return true;
            }
        }
        return false;
    }
}
