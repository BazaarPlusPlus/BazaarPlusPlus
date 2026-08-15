#nullable enable
using BazaarGameShared.Domain.Core.Types;

namespace BazaarPlusPlus.Game.CollectionPanel.Data;

// Presentation identity for the shared keyword/mechanic facet. Cards retain their native hidden
// keyword collection and compact mechanic flags separately; only the small UI option list is
// unified, preventing a per-card union allocation.
internal readonly record struct CollectionKeywordFacetOption
{
    private CollectionKeywordFacetOption(EHiddenTag? keyword, CollectionMechanic? mechanic)
    {
        Keyword = keyword;
        Mechanic = mechanic;
    }

    public EHiddenTag? Keyword { get; }

    public CollectionMechanic? Mechanic { get; }

    public bool IsRelated =>
        Keyword.HasValue && CollectionKeywordWhitelist.IsRelatedKeyword(Keyword.Value);

    public static CollectionKeywordFacetOption ForKeyword(EHiddenTag keyword) => new(keyword, null);

    public static CollectionKeywordFacetOption ForMechanic(CollectionMechanic mechanic) =>
        new(null, mechanic);

    public override string ToString() =>
        Keyword?.ToString() ?? Mechanic?.ToString() ?? string.Empty;
}

internal static class CollectionKeywordFacetPresentation
{
    public static IReadOnlyList<CollectionKeywordFacetOption> Ordered(
        IReadOnlyList<EHiddenTag> keywords,
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
