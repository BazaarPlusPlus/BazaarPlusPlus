#nullable enable
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Game.CardTags;

namespace BazaarPlusPlus.Game.CollectionPanel.Data;

// Facet availability is a pure projection of the immutable built catalog, so it can be
// computed once per catalog (one scan covering every facet the panel renders) instead of
// re-scanning every card on each RefreshView.
internal sealed class CollectionFacetAvailabilitySnapshot
{
    public static readonly CollectionFacetAvailabilitySnapshot Empty = new(
        Array.Empty<ECardTag>(),
        Array.Empty<ECardTag>(),
        Array.Empty<EHiddenTag>(),
        Array.Empty<EHiddenTag>(),
        Array.Empty<CollectionMechanic>(),
        Array.Empty<CollectionMechanic>()
    );

    public CollectionFacetAvailabilitySnapshot(
        IReadOnlyList<ECardTag> itemTags,
        IReadOnlyList<ECardTag> skillTags,
        IReadOnlyList<EHiddenTag> itemKeywords,
        IReadOnlyList<EHiddenTag> skillKeywords,
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

    public IReadOnlyList<ECardTag> ItemTags { get; }
    public IReadOnlyList<ECardTag> SkillTags { get; }
    public IReadOnlyList<EHiddenTag> ItemKeywords { get; }
    public IReadOnlyList<EHiddenTag> SkillKeywords { get; }
    public IReadOnlyList<CollectionMechanic> ItemMechanics { get; }
    public IReadOnlyList<CollectionMechanic> SkillMechanics { get; }
    public IReadOnlyList<CollectionKeywordFacetOption> ItemKeywordOptions { get; }
    public IReadOnlyList<CollectionKeywordFacetOption> SkillKeywordOptions { get; }

    // Non-Skill maps to Item, mirroring CollectionTabProfile.For.
    public IReadOnlyList<ECardTag> TagsFor(ECardType type) =>
        type == ECardType.Skill ? SkillTags : ItemTags;

    public IReadOnlyList<EHiddenTag> KeywordsFor(ECardType type) =>
        type == ECardType.Skill ? SkillKeywords : ItemKeywords;

    public IReadOnlyList<CollectionMechanic> MechanicsFor(ECardType type) =>
        type == ECardType.Skill ? SkillMechanics : ItemMechanics;

    public IReadOnlyList<CollectionKeywordFacetOption> KeywordOptionsFor(ECardType type) =>
        type == ECardType.Skill ? SkillKeywordOptions : ItemKeywordOptions;
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
        var skillTags = new HashSet<ECardTag>();
        var itemKeywords = new HashSet<EHiddenTag>();
        var skillKeywords = new HashSet<EHiddenTag>();
        var itemMechanics = new HashSet<CollectionMechanic>();
        var skillMechanics = new HashSet<CollectionMechanic>();
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
                AddMechanics(itemMechanics, card.Mechanics);
            }
            else if (card.Type == ECardType.Skill)
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

    private static IReadOnlyList<ECardTag> OrderedTags(HashSet<ECardTag> present)
    {
        var available = new List<ECardTag>(PlayerFacingCardTags.Ordered.Count);
        foreach (var tag in PlayerFacingCardTags.Ordered)
            if (present.Contains(tag))
                available.Add(tag);
        return available;
    }

    private static IReadOnlyList<EHiddenTag> OrderedKeywords(HashSet<EHiddenTag> present)
    {
        var available = new List<EHiddenTag>(CollectionKeywordWhitelist.Ordered.Count);
        foreach (var keyword in CollectionKeywordWhitelist.Ordered)
            if (
                present.Contains(keyword)
                && !CollectionMechanicFacts.TryFromHiddenTag(keyword, out _)
            )
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
