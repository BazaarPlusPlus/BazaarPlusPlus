#nullable enable
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Game.CollectionPanel.Sources;

namespace BazaarPlusPlus.Game.CollectionPanel.Data;

internal interface ICollectionOfferPoolResolver
{
    CollectionSourceOfferPoolResult GetOrResolve(
        CollectionSourceEntry source,
        EHero? selectedHero,
        IReadOnlyList<CollectionCardVm> catalogCards
    );
}

internal interface ICollectionSourceCatalog
{
    bool TryGetBySourceKey(string sourceKey, out CollectionSourceEntry? entry);
}

internal sealed class CollectionFilterNormalization
{
    public CollectionFilterNormalization(
        bool clearSelectedSource,
        IReadOnlyCollection<ECardTag>? retainedTags,
        IReadOnlyCollection<EHiddenTag>? retainedKeywords
    )
    {
        ClearSelectedSource = clearSelectedSource;
        RetainedTags = retainedTags;
        RetainedKeywords = retainedKeywords;
    }

    public bool ClearSelectedSource { get; }

    public IReadOnlyCollection<ECardTag>? RetainedTags { get; }

    public IReadOnlyCollection<EHiddenTag>? RetainedKeywords { get; }
}

internal sealed class CollectionQueryResult
{
    public CollectionQueryResult(
        IReadOnlyList<CollectionCardVm> cards,
        IReadOnlyDictionary<Guid, IReadOnlyList<CollectionSourceOfferMatch>>? offerMatchesByCardId,
        CollectionFilterNormalization normalization
    )
    {
        Cards = cards;
        OfferMatchesByCardId = offerMatchesByCardId;
        Normalization = normalization;
    }

    public IReadOnlyList<CollectionCardVm> Cards { get; }

    public IReadOnlyDictionary<
        Guid,
        IReadOnlyList<CollectionSourceOfferMatch>
    >? OfferMatchesByCardId { get; }

    public CollectionFilterNormalization Normalization { get; }
}

internal static class CollectionQuery
{
    public static CollectionQueryResult Run(
        IReadOnlyList<CollectionCardVm> catalogCards,
        CollectionFilterState filter,
        CollectionFacetAvailabilitySnapshot facetAvailability,
        ICollectionSourceCatalog sourceCatalog,
        ICollectionOfferPoolResolver offerPoolResolver
    )
    {
        if (catalogCards == null)
            throw new ArgumentNullException(nameof(catalogCards));
        if (filter == null)
            throw new ArgumentNullException(nameof(filter));
        if (facetAvailability == null)
            throw new ArgumentNullException(nameof(facetAvailability));
        if (sourceCatalog == null)
            throw new ArgumentNullException(nameof(sourceCatalog));
        if (offerPoolResolver == null)
            throw new ArgumentNullException(nameof(offerPoolResolver));

        var retainedTags = RetainedTags(filter, facetAvailability);
        var retainedKeywords = RetainedKeywords(filter, facetAvailability);
        var queryFilter =
            retainedTags == null && retainedKeywords == null
                ? filter
                : CloneFilter(filter, retainedTags, retainedKeywords);

        var sourceResolution = ResolveSelectedSource(filter, sourceCatalog);
        IReadOnlyCollection<Guid>? offeredCardIds = null;
        IReadOnlyDictionary<Guid, IReadOnlyList<CollectionSourceOfferMatch>>? offerMatchesByCardId =
            null;
        if (sourceResolution.Source != null)
        {
            var offerPoolResult = offerPoolResolver.GetOrResolve(
                sourceResolution.Source,
                filter.SelectedHero,
                catalogCards
            );
            if (offerPoolResult.Status == CollectionSourceOfferPoolStatus.Ready)
            {
                offeredCardIds = offerPoolResult.OfferedCardIds;
                offerMatchesByCardId = offerPoolResult.OfferMatchesByCardId;
            }
        }

        var hasSelectedSource = sourceResolution.Source != null;
        var ordered = CollectionFilterEngine.Apply(
            catalogCards,
            queryFilter,
            new CollectionFilterContext
            {
                OfferedCardIds = offeredCardIds,
                ApplyHeroFilter =
                    !hasSelectedSource || filter.ActiveTab == CollectionTabKind.Skills,
                SuppressDayGate =
                    offeredCardIds != null && sourceResolution.Source!.SuppressDayGate,
            }
        );
        var normalization = new CollectionFilterNormalization(
            sourceResolution.ClearSelectedSource,
            retainedTags,
            retainedKeywords
        );
        return new CollectionQueryResult(ordered, offerMatchesByCardId, normalization);
    }

    private static IReadOnlyCollection<ECardTag>? RetainedTags(
        CollectionFilterState filter,
        CollectionFacetAvailabilitySnapshot facetAvailability
    )
    {
        var profile = CollectionTabProfile.For(filter.ActiveTab);
        if (!profile.ShowTagFilter || filter.Tags.Count == 0)
            return null;
        return RetainedSet(filter.Tags, facetAvailability.ItemTags);
    }

    private static IReadOnlyCollection<EHiddenTag>? RetainedKeywords(
        CollectionFilterState filter,
        CollectionFacetAvailabilitySnapshot facetAvailability
    )
    {
        var profile = CollectionTabProfile.For(filter.ActiveTab);
        if (!profile.ShowKeywordFilter || filter.Keywords.Count == 0)
            return null;
        return RetainedSet(filter.Keywords, facetAvailability.KeywordsFor(filter.ActiveType));
    }

    private static IReadOnlyCollection<T> RetainedSet<T>(
        HashSet<T> selected,
        IReadOnlyList<T> available
    )
    {
        var allowed = available as HashSet<T> ?? new HashSet<T>(available);
        var retained = new List<T>(selected.Count);
        foreach (var value in selected)
        {
            if (allowed.Contains(value))
                retained.Add(value);
        }
        return retained;
    }

    private static CollectionFilterState CloneFilter(
        CollectionFilterState source,
        IReadOnlyCollection<ECardTag>? retainedTags,
        IReadOnlyCollection<EHiddenTag>? retainedKeywords
    )
    {
        var clone = new CollectionFilterState
        {
            ActiveType = source.ActiveType,
            SelectedSourceKey = source.SelectedSourceKey,
            SearchQuery = source.SearchQuery,
            SelectedRunDay = source.SelectedRunDay,
            SortPriority = source.SortPriority,
            TagMatchMode = source.TagMatchMode,
            KeywordMatchMode = source.KeywordMatchMode,
        };
        clone.Heroes.UnionWith(source.Heroes);
        clone.Tiers.UnionWith(source.Tiers);
        clone.Sizes.UnionWith(source.Sizes);
        clone.Tags.UnionWith(retainedTags ?? source.Tags);
        clone.Keywords.UnionWith(retainedKeywords ?? source.Keywords);
        return clone;
    }

    private static SourceResolution ResolveSelectedSource(
        CollectionFilterState filter,
        ICollectionSourceCatalog sourceCatalog
    )
    {
        var sourceKey = filter.SelectedSourceKey;
        if (string.IsNullOrWhiteSpace(sourceKey))
            return SourceResolution.Unselected;

        if (!sourceCatalog.TryGetBySourceKey(sourceKey!, out var entry) || entry == null)
            return SourceResolution.Clear;

        var expectedKind = CollectionTabProfile.For(filter.ActiveTab).SourceKind;
        if (!expectedKind.HasValue || entry.Kind != expectedKind.Value)
            return SourceResolution.Clear;

        var selectedHero = filter.SelectedHero;
        if (selectedHero.HasValue && !entry.AppliesToHero(selectedHero.Value))
            return SourceResolution.Clear;

        return new SourceResolution(entry, false);
    }

    private readonly struct SourceResolution
    {
        public static readonly SourceResolution Unselected = new(null, false);
        public static readonly SourceResolution Clear = new(null, true);

        public SourceResolution(CollectionSourceEntry? source, bool clearSelectedSource)
        {
            Source = source;
            ClearSelectedSource = clearSelectedSource;
        }

        public CollectionSourceEntry? Source { get; }

        public bool ClearSelectedSource { get; }
    }
}
