#nullable enable
using System;
using System.Collections.Generic;
using BazaarGameShared.Domain.Core.Types;

namespace BazaarPlusPlus.Game.CollectionPanel.Data;

// Facet availability is a pure projection of the immutable built catalog, so it can be
// computed once per catalog (one scan covering every facet the panel renders) instead of
// re-scanning every card on each RefreshView.
internal sealed class CollectionFacetAvailabilitySnapshot
{
    public static readonly CollectionFacetAvailabilitySnapshot Empty = new(
        Array.Empty<ECardTag>(),
        Array.Empty<EHiddenTag>(),
        Array.Empty<EHiddenTag>()
    );

    public CollectionFacetAvailabilitySnapshot(
        IReadOnlyList<ECardTag> itemTags,
        IReadOnlyList<EHiddenTag> itemKeywords,
        IReadOnlyList<EHiddenTag> skillKeywords
    )
    {
        ItemTags = itemTags;
        ItemKeywords = itemKeywords;
        SkillKeywords = skillKeywords;
    }

    public IReadOnlyList<ECardTag> ItemTags { get; }
    public IReadOnlyList<EHiddenTag> ItemKeywords { get; }
    public IReadOnlyList<EHiddenTag> SkillKeywords { get; }

    // Non-Skill maps to Item, mirroring CollectionTabProfile.For.
    public IReadOnlyList<EHiddenTag> KeywordsFor(ECardType type) =>
        type == ECardType.Skill ? SkillKeywords : ItemKeywords;
}

internal static class CollectionFacetAvailability
{
    public static CollectionFacetAvailabilitySnapshot SnapshotFor(
        IReadOnlyList<CollectionCardVm> cards
    )
    {
        if (cards.Count == 0)
            return CollectionFacetAvailabilitySnapshot.Empty;

        var itemTags = new HashSet<ECardTag>();
        var itemKeywords = new HashSet<EHiddenTag>();
        var skillKeywords = new HashSet<EHiddenTag>();
        foreach (var card in cards)
        {
            if (card.IsPackage)
                continue;
            if (card.Type == ECardType.Item)
            {
                foreach (var tag in card.Tags)
                    itemTags.Add(tag);
                foreach (var keyword in card.HiddenTags)
                    itemKeywords.Add(keyword);
            }
            else if (card.Type == ECardType.Skill)
            {
                foreach (var keyword in card.HiddenTags)
                    skillKeywords.Add(keyword);
            }
        }

        return new CollectionFacetAvailabilitySnapshot(
            OrderedTags(itemTags),
            OrderedKeywords(itemKeywords),
            OrderedKeywords(skillKeywords)
        );
    }

    public static IReadOnlyList<ECardTag> TagsFor(
        IReadOnlyList<CollectionCardVm> cards,
        ECardType type
    )
    {
        if (cards.Count == 0)
            return Array.Empty<ECardTag>();

        var present = new HashSet<ECardTag>();
        foreach (var card in cards)
        {
            if (!IsFacetSource(card, type))
                continue;
            foreach (var tag in card.Tags)
                present.Add(tag);
        }

        return OrderedTags(present);
    }

    public static IReadOnlyList<EHiddenTag> KeywordsFor(
        IReadOnlyList<CollectionCardVm> cards,
        ECardType type
    )
    {
        if (cards.Count == 0)
            return Array.Empty<EHiddenTag>();

        var present = new HashSet<EHiddenTag>();
        foreach (var card in cards)
        {
            if (!IsFacetSource(card, type))
                continue;
            foreach (var keyword in card.HiddenTags)
                present.Add(keyword);
        }

        return OrderedKeywords(present);
    }

    private static IReadOnlyList<ECardTag> OrderedTags(HashSet<ECardTag> present)
    {
        var available = new List<ECardTag>(CollectionTagWhitelist.Ordered.Count);
        foreach (var tag in CollectionTagWhitelist.Ordered)
            if (present.Contains(tag))
                available.Add(tag);
        return available;
    }

    private static IReadOnlyList<EHiddenTag> OrderedKeywords(HashSet<EHiddenTag> present)
    {
        var available = new List<EHiddenTag>(CollectionKeywordWhitelist.Ordered.Count);
        foreach (var keyword in CollectionKeywordWhitelist.Ordered)
            if (present.Contains(keyword))
                available.Add(keyword);
        return available;
    }

    private static bool IsFacetSource(CollectionCardVm card, ECardType type) =>
        card.Type == type && !card.IsPackage;
}
