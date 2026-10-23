#nullable enable
using BazaarPlusPlus.Core.Cards;

namespace BazaarPlusPlus.Game.CollectionPanel.Data;

// Facet availability is a pure projection of the immutable built catalog, so it can be
// computed once per catalog (one scan covering every facet the panel renders) instead of
// re-scanning every card on each RefreshView.
public sealed class CollectionFacetAvailabilitySnapshot
{
    public static readonly CollectionFacetAvailabilitySnapshot Empty = new(
        Array.Empty<CardTag>(),
        Array.Empty<CardTag>(),
        Array.Empty<CardHiddenTag>(),
        Array.Empty<CardHiddenTag>(),
        Array.Empty<CollectionMechanic>(),
        Array.Empty<CollectionMechanic>()
    );

    public CollectionFacetAvailabilitySnapshot(
        IReadOnlyList<CardTag> itemTags,
        IReadOnlyList<CardTag> skillTags,
        IReadOnlyList<CardHiddenTag> itemKeywords,
        IReadOnlyList<CardHiddenTag> skillKeywords,
        IReadOnlyList<CollectionMechanic> itemMechanics,
        IReadOnlyList<CollectionMechanic> skillMechanics
    )
    {
        ItemTags = itemTags;
        SkillTags = skillTags;
        ItemKeywords = itemKeywords;
        SkillKeywords = skillKeywords;
        ItemMechanics = itemMechanics;
        SkillMechanics = skillMechanics;
        ItemKeywordOptions = CollectionKeywordFacetPresentation.Ordered(
            itemKeywords,
            itemMechanics
        );
        SkillKeywordOptions = CollectionKeywordFacetPresentation.Ordered(
            skillKeywords,
            skillMechanics
        );
    }

    public IReadOnlyList<CardTag> ItemTags { get; }
    public IReadOnlyList<CardTag> SkillTags { get; }
    public IReadOnlyList<CardHiddenTag> ItemKeywords { get; }
    public IReadOnlyList<CardHiddenTag> SkillKeywords { get; }
    public IReadOnlyList<CollectionMechanic> ItemMechanics { get; }
    public IReadOnlyList<CollectionMechanic> SkillMechanics { get; }
    public IReadOnlyList<CollectionKeywordFacetOption> ItemKeywordOptions { get; }
    public IReadOnlyList<CollectionKeywordFacetOption> SkillKeywordOptions { get; }

    // Non-Skill maps to Item, mirroring CollectionTabProfile.For.
    public IReadOnlyList<CardTag> TagsFor(CardType type) =>
        type == CardType.Skill ? SkillTags : ItemTags;

    public IReadOnlyList<CardHiddenTag> KeywordsFor(CardType type) =>
        type == CardType.Skill ? SkillKeywords : ItemKeywords;

    public IReadOnlyList<CollectionMechanic> MechanicsFor(CardType type) =>
        type == CardType.Skill ? SkillMechanics : ItemMechanics;

    public IReadOnlyList<CollectionKeywordFacetOption> KeywordOptionsFor(CardType type) =>
        type == CardType.Skill ? SkillKeywordOptions : ItemKeywordOptions;
}

public static class CollectionFacetAvailability
{
    public static CollectionFacetAvailabilitySnapshot SnapshotFor(
        IReadOnlyList<CollectionCardVm> cards
    )
    {
        if (cards.Count == 0)
            return CollectionFacetAvailabilitySnapshot.Empty;

        var itemTags = new HashSet<CardTag>();
        var skillTags = new HashSet<CardTag>();
        var itemKeywords = new HashSet<CardHiddenTag>();
        var skillKeywords = new HashSet<CardHiddenTag>();
        var itemMechanics = new HashSet<CollectionMechanic>();
        var skillMechanics = new HashSet<CollectionMechanic>();
        foreach (var card in cards)
        {
            if (card.IsPackage)
                continue;
            if (card.Type == CardType.Item)
            {
                foreach (var tag in card.Tags)
                    itemTags.Add(tag);
                foreach (var keyword in card.HiddenTags)
                    itemKeywords.Add(keyword);
                AddMechanics(itemMechanics, card.Mechanics);
            }
            else if (card.Type == CardType.Skill)
            {
                foreach (var tag in card.Tags)
                    skillTags.Add(tag);
                foreach (var keyword in card.HiddenTags)
                    skillKeywords.Add(keyword);
                AddMechanics(skillMechanics, card.Mechanics);
            }
        }

        return new CollectionFacetAvailabilitySnapshot(
            OrderedTags(itemTags),
            OrderedTags(skillTags),
            OrderedKeywords(itemKeywords),
            OrderedKeywords(skillKeywords),
            OrderedMechanics(itemMechanics),
            OrderedMechanics(skillMechanics)
        );
    }

    private static IReadOnlyList<CardTag> OrderedTags(HashSet<CardTag> present)
    {
        var available = new List<CardTag>(CardFacetOrder.PlayerFacingTags.Count);
        foreach (var tag in CardFacetOrder.PlayerFacingTags)
            if (present.Contains(tag))
                available.Add(tag);
        return available;
    }

    private static IReadOnlyList<CardHiddenTag> OrderedKeywords(HashSet<CardHiddenTag> present)
    {
        var available = new List<CardHiddenTag>(CollectionKeywordWhitelist.Ordered.Count);
        foreach (var keyword in CollectionKeywordWhitelist.Ordered)
            if (present.Contains(keyword) && !CollectionMechanics.TryFromHiddenTag(keyword, out _))
                available.Add(keyword);
        return available;
    }

    private static void AddMechanics(HashSet<CollectionMechanic> present, CollectionMechanic facts)
    {
        foreach (var mechanic in CollectionMechanics.Ordered)
            if (facts.Has(mechanic))
                present.Add(mechanic);
    }

    private static IReadOnlyList<CollectionMechanic> OrderedMechanics(
        HashSet<CollectionMechanic> present
    )
    {
        var available = new List<CollectionMechanic>(CollectionMechanics.Ordered.Count);
        foreach (var mechanic in CollectionMechanics.Ordered)
            if (present.Contains(mechanic))
                available.Add(mechanic);
        return available;
    }
}
