#nullable enable
using BazaarPlusPlus.Core.Cards;

namespace BazaarPlusPlus.Game.CollectionPanel.Data;

// Unify the filter options at the UI boundary to avoid allocating a union for every card.
public readonly record struct CollectionKeywordFacetOption
{
    private CollectionKeywordFacetOption(CardHiddenTag? keyword, CollectionMechanic? mechanic)
    {
        Keyword = keyword;
        Mechanic = mechanic;
    }

    public CardHiddenTag? Keyword { get; }

    public CollectionMechanic? Mechanic { get; }

    public bool IsRelated =>
        Keyword.HasValue && CollectionKeywordWhitelist.IsRelatedKeyword(Keyword.Value);

    public static CollectionKeywordFacetOption ForKeyword(CardHiddenTag keyword) =>
        new(keyword, null);

    public static CollectionKeywordFacetOption ForMechanic(CollectionMechanic mechanic) =>
        new(null, mechanic);

    public override string ToString() =>
        Keyword?.ToString() ?? Mechanic?.ToString() ?? string.Empty;
}

public static class CollectionKeywordFacetPresentation
{
    public static IReadOnlyList<CollectionKeywordFacetOption> Ordered(
        IReadOnlyList<CardHiddenTag> keywords,
        IReadOnlyList<CollectionMechanic> mechanics
    )
    {
        var options = new List<CollectionKeywordFacetOption>(keywords.Count + mechanics.Count);
        foreach (var keyword in keywords)
            if (!CollectionKeywordWhitelist.IsRelatedKeyword(keyword))
                options.Add(CollectionKeywordFacetOption.ForKeyword(keyword));
        foreach (var mechanic in mechanics)
            options.Add(CollectionKeywordFacetOption.ForMechanic(mechanic));
        foreach (var keyword in keywords)
            if (CollectionKeywordWhitelist.IsRelatedKeyword(keyword))
                options.Add(CollectionKeywordFacetOption.ForKeyword(keyword));
        return options;
    }
}
