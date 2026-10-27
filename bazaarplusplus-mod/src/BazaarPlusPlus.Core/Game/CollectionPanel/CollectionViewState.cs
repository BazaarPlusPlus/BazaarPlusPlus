#nullable enable
using BazaarPlusPlus.Core.Cards;
using BazaarPlusPlus.Game.CollectionPanel.Data;
using BazaarPlusPlus.Game.CollectionPanel.Sources;
using BazaarPlusPlus.Game.CollectionPanel.Ui;
using BazaarPlusPlus.Game.Supporters;

namespace BazaarPlusPlus.Game.CollectionPanel;

// Single owner of Collection Panel presentable state: filter selections, search mode/debounce,
// catalog acceptance, and the derived render model. Commands and lifecycle events go in; a
// complete render outcome (view model plus scroll intents) comes out. Pure — no Unity types.
// Open and Close are the panel's lifecycle; catalog arrival is AcceptCatalog / CatalogUnavailable /
// BeginCatalogLoad.
public sealed class CollectionViewState
{
    private const float SearchRefreshDebounceSeconds = 0.16f;

    private static readonly CardTier[] TierOrder =
    [
        CardTier.Bronze,
        CardTier.Silver,
        CardTier.Gold,
        CardTier.Diamond,
        CardTier.Legendary,
    ];

    private static readonly CardSize[] SizeOrder =
    [
        CardSize.Small,
        CardSize.Medium,
        CardSize.Large,
    ];

    private readonly ICollectionGridPort _grid;
    private readonly ICollectionSourceCatalog _sourceCatalog;
    private readonly ICollectionDayTierSource _dayTier;
    private readonly ICollectionPanelHeroPreferenceStore _heroPreferenceStore;

    private readonly CollectionFilterState _filter = new();
    private readonly CollectionSearchModeState _searchMode = new();
    private readonly CollectionSearchRefreshGate _searchRefreshGate = new(
        SearchRefreshDebounceSeconds
    );
    private readonly CollectionSourceOfferPoolCache _offerPoolCache = new();

    private IReadOnlyList<CollectionCardVm> _catalogCards = Array.Empty<CollectionCardVm>();
    private CollectionFacetAvailabilitySnapshot _facetAvailability =
        CollectionFacetAvailabilitySnapshot.Empty;
    private CollectionCatalogReadiness _catalogReadiness = CollectionCatalogReadiness.Loading;
    private IReadOnlyList<CardHero> _availableHeroes =
        CollectionHeroSelectionRoster.BaseConcreteHeroes;

    private (
        CollectionSourceKind Kind,
        CardHero Hero,
        IReadOnlyList<CollectionSourceOptionViewModel> Sources
    )? _availableSourcesCache;

    private IReadOnlyList<BPPSupporterSample> _supporters = Array.Empty<BPPSupporterSample>();
    private readonly HashSet<string> _encounteredMerchantSourceKeys = new(StringComparer.Ordinal);
    private int? _currentRunDay;
    private bool _isLoadingCatalog;
    private string? _statusMessage;
    private bool _statusVisible;

    public CollectionViewState(
        ICollectionGridPort grid,
        ICollectionSourceCatalog sourceCatalog,
        ICollectionDayTierSource dayTier,
        ICollectionPanelHeroPreferenceStore heroPreferenceStore
    )
    {
        _grid = grid ?? throw new ArgumentNullException(nameof(grid));
        _sourceCatalog = sourceCatalog ?? throw new ArgumentNullException(nameof(sourceCatalog));
        _dayTier = dayTier ?? throw new ArgumentNullException(nameof(dayTier));
        _heroPreferenceStore =
            heroPreferenceStore ?? throw new ArgumentNullException(nameof(heroPreferenceStore));
    }

    public CollectionCatalogReadiness CatalogReadiness => _catalogReadiness;

    public int CatalogCardCount => _catalogCards.Count;

    // The whole open order: search reset, catalog readiness from the host's cached catalog, the
    // remembered-hero load (out of a run only), the open selection, and its apply. Never
    // publishes, never saves the hero preference, and never accepts the catalog: the host
    // delivers it afterwards with AcceptCatalog, CatalogUnavailable or BeginCatalogLoad.
    public CollectionOpenOutcome Open(CollectionOpenFacts facts)
    {
        if (facts == null)
            throw new ArgumentNullException(nameof(facts));

        ResetSearchForLifecycle();
        if (facts.CachedCatalog != null)
            SetCatalogState(CollectionCatalogReadiness.Accepted, facts.CachedCatalog);
        else
            SetCatalogState(CollectionCatalogReadiness.Loading, Array.Empty<CollectionCardVm>());

        // Readiness is set before the load: the store resolves a stored hero against it.
        CollectionPanelHeroPreferenceLoadResult? remembered = null;
        Exception? heroPreferenceFailure = null;
        if (!facts.IsInGameRun)
        {
            try
            {
                remembered = _heroPreferenceStore.Load(_catalogReadiness, _availableHeroes);
            }
            catch (Exception ex)
            {
                heroPreferenceFailure = ex;
            }
        }

        var encounter = facts.Encounter;
        var entries = _sourceCatalog.Entries;
        var selection = CollectionPanelOpenSelectionResolver.Resolve(
            facts.IsInGameRun,
            facts.Hero,
            encounter.CurrentEncounterTemplateId,
            encounter.ChoiceSelectionTemplateIds,
            entries,
            remembered
        );
        var encounteredMerchantSourceKeys = facts.IsInGameRun
            ? CollectionPanelOpenSelectionResolver.ResolveEncounteredMerchantSourceKeys(
                facts.Hero,
                encounter.ChoiceSelectionTemplateIds,
                entries
            )
            : Array.Empty<string>();

        _filter.ApplySelection(selection);
        if (_catalogReadiness == CollectionCatalogReadiness.Accepted)
            PruneInvisibleSourceSelections();

        _supporters = facts.Supporters;
        _encounteredMerchantSourceKeys.Clear();
        foreach (var sourceKey in encounteredMerchantSourceKeys)
            if (!string.IsNullOrWhiteSpace(sourceKey))
                _encounteredMerchantSourceKeys.Add(sourceKey.Trim());
        // Holds until the first catalog query re-reads the day from the day-tier port.
        _currentRunDay = facts.RunDay;
        var render = new CollectionRenderOutcome(
            BuildModel(_grid.Current),
            resetScroll: true,
            resetControlsScroll: true
        );
        return new CollectionOpenOutcome(render, selection, heroPreferenceFailure);
    }

    // Resets the search (default expanded mode, empty query, no pending debounce) and clears the
    // loading flag. The render is built before the flag is cleared, so it shows the loading state
    // the panel closes with. Never publishes and leaves readiness, the filter, the cards and the
    // preference alone; harmless while closed.
    public CollectionRenderOutcome Close()
    {
        ResetSearchForLifecycle();
        var outcome = new CollectionRenderOutcome(
            BuildModel(_grid.Current),
            resetScroll: false,
            resetControlsScroll: false
        );
        _isLoadingCatalog = false;
        return outcome;
    }

    public CollectionRenderOutcome? SetActiveTab(CollectionTabKind tab)
    {
        if (!_filter.SelectTab(tab))
            return null;

        PruneInvisibleSourceSelections();
        return QueryAndRender(resetControlsScroll: true);
    }

    public CollectionRenderOutcome? ToggleHero(CardHero hero)
    {
        var effective = _filter.ToggleHero(hero);
        _heroPreferenceStore.Save(effective);
        PruneInvisibleSourceSelections();
        return QueryAndRender(resetControlsScroll: true);
    }

    public CollectionRenderOutcome ToggleAllHeroes()
    {
        if (_filter.ToggleAllHeroes())
        {
            // A selected merchant/trainer resolves one concrete hero's offer pool before the
            // hero predicate runs. Leaving it selected would make an all-heroes scope appear
            // active while still showing only that prior hero's cards.
            _filter.ClearSelectedSource();
        }
        else
        {
            PruneInvisibleSourceSelections();
        }
        return QueryAndRender(resetControlsScroll: true);
    }

    public CollectionRenderOutcome? ToggleTier(CardTier tier)
    {
        if (_filter.Tiers.Count == 1 && _filter.Tiers.Contains(tier))
            _filter.Tiers.Clear();
        else
        {
            _filter.Tiers.Clear();
            _filter.Tiers.Add(tier);
        }
        return QueryAndRender(resetControlsScroll: true);
    }

    public CollectionRenderOutcome? ToggleRunDayFilter()
    {
        _filter.UseRunDayFilter = !_filter.UseRunDayFilter;
        return QueryAndRender(resetControlsScroll: false);
    }

    public CollectionRenderOutcome? ToggleSize(CardSize size)
    {
        if (_filter.Sizes.Count == 1 && _filter.Sizes.Contains(size))
            _filter.Sizes.Clear();
        else
        {
            _filter.Sizes.Clear();
            _filter.Sizes.Add(size);
        }
        return QueryAndRender(resetControlsScroll: false);
    }

    public CollectionRenderOutcome? ToggleTag(CardTag tag)
    {
        if (!_filter.Tags.Remove(tag))
            _filter.Tags.Add(tag);
        return QueryAndRender(resetControlsScroll: false);
    }

    public CollectionRenderOutcome? ToggleKeyword(CollectionKeywordFacetOption option)
    {
        if (option.Keyword.HasValue)
        {
            if (!_filter.Keywords.Remove(option.Keyword.Value))
                _filter.Keywords.Add(option.Keyword.Value);
        }
        else if (option.Mechanic.HasValue)
        {
            if (!_filter.Mechanics.Remove(option.Mechanic.Value))
                _filter.Mechanics.Add(option.Mechanic.Value);
        }

        return QueryAndRender(resetControlsScroll: false);
    }

    public CollectionRenderOutcome? SetKeywordMatchMode(CollectionFacetMatchMode mode)
    {
        _filter.KeywordMatchMode = mode;
        return QueryAndRender(resetControlsScroll: false);
    }

    public CollectionRenderOutcome? SetTagMatchMode(CollectionFacetMatchMode mode)
    {
        _filter.TagMatchMode = mode;
        return QueryAndRender(resetControlsScroll: false);
    }

    public CollectionRenderOutcome ResetFilters()
    {
        _filter.ResetFacets();
        _searchRefreshGate.Cancel();
        _heroPreferenceStore.Save(_filter.EffectiveHero);
        return QueryAndRender(resetControlsScroll: true);
    }

    public CollectionRenderOutcome? ToggleSource(string sourceKey)
    {
        _filter.ToggleSource(_filter.ActiveTab, sourceKey);
        return QueryAndRender(resetControlsScroll: false);
    }

    public CollectionRenderOutcome? SetSortPriority(CollectionSortPriority priority)
    {
        if (_filter.SortPriority == priority)
            return null;

        _filter.SortPriority = priority;
        return QueryAndRender(resetControlsScroll: false);
    }

    public void SetSearchQuery(string query)
    {
        query ??= string.Empty;
        if (string.Equals(_filter.SearchQuery, query, StringComparison.Ordinal))
            return;

        _filter.SearchQuery = query;
        _searchRefreshGate.Schedule();
    }

    public CollectionRenderOutcome ToggleSearch()
    {
        var queryChanged = _searchMode.IsExpanded
            ? _searchMode.Collapse(_filter)
            : _searchMode.Expand(_filter);
        _searchRefreshGate.Cancel();
        if (queryChanged)
            return QueryAndRender(resetControlsScroll: false);

        return new CollectionRenderOutcome(
            BuildModel(_grid.Current),
            resetScroll: false,
            resetControlsScroll: false
        );
    }

    public CollectionRenderOutcome? TickSearch(float dt, bool isComposing)
    {
        if (!_searchRefreshGate.Advance(dt, isComposing))
            return null;

        return QueryAndRender(resetControlsScroll: false);
    }

    public CollectionRenderOutcome BeginCatalogLoad()
    {
        _isLoadingCatalog = true;
        SetStatus(CollectionPanelText.CatalogLoading());
        var projection = _grid.Publish(Array.Empty<CollectionCardVm>(), _filter.ActiveTab);
        return new CollectionRenderOutcome(
            BuildModel(projection ?? CollectionGridProjection.Empty),
            resetScroll: true,
            resetControlsScroll: false
        );
    }

    public CollectionRenderOutcome AcceptCatalog(IReadOnlyList<CollectionCardVm> cards)
    {
        if (cards == null)
            throw new ArgumentNullException(nameof(cards));

        // An Open from the host's cached catalog already accepted this list: its heroes and facet
        // snapshot stand, and only the source options are recomputed. The query still runs here,
        // since Open renders without one.
        if (
            _catalogReadiness == CollectionCatalogReadiness.Accepted
            && ReferenceEquals(cards, _catalogCards)
        )
            _availableSourcesCache = null;
        else
            SetCatalogState(CollectionCatalogReadiness.Accepted, cards);
        NormalizeHeroSelectionWithoutSave();
        PruneInvisibleSourceSelections();
        return FinishCatalogLoad();
    }

    public CollectionRenderOutcome CatalogUnavailable()
    {
        SetCatalogState(CollectionCatalogReadiness.Unavailable, Array.Empty<CollectionCardVm>());
        SetStatus(CollectionPanelText.CatalogUnavailable());
        return FinishCatalogLoad();
    }

    public void ResetCatalog()
    {
        SetCatalogState(CollectionCatalogReadiness.Loading, Array.Empty<CollectionCardVm>());
        _offerPoolCache.Clear();
        _availableSourcesCache = null;
    }

    public CollectionRenderOutcome Rebuild() =>
        new(BuildModel(_grid.Current), resetScroll: false, resetControlsScroll: false);

    private void ResetSearchForLifecycle()
    {
        _searchMode.Reset(_filter);
        _searchRefreshGate.Cancel();
    }

    private CollectionRenderOutcome FinishCatalogLoad()
    {
        _isLoadingCatalog = false;
        if (_catalogCards.Count > 0)
            ClearStatus();
        return QueryAndRender(resetControlsScroll: false);
    }

    private CollectionRenderOutcome QueryAndRender(bool resetControlsScroll)
    {
        // Debounce cancel happens only after a confirmed state change (callers already decided
        // the command is not a same-value no-op). Matches prior ApplyFilters entry.
        _searchRefreshGate.Cancel();

        var projection = RunQueryAndPublish();
        if (projection == null)
        {
            // Grid unavailable: skip normalization write-back; still emit a model so lifecycle
            // renders can update chrome when the view exists without a virtualizer.
            return new CollectionRenderOutcome(
                BuildModel(CollectionGridProjection.Empty),
                resetScroll: true,
                resetControlsScroll
            );
        }

        return new CollectionRenderOutcome(
            BuildModel(projection.Value),
            resetScroll: true,
            resetControlsScroll
        );
    }

    private CollectionGridProjection? RunQueryAndPublish()
    {
        if (_catalogCards.Count == 0)
        {
            return _grid.Publish(Array.Empty<CollectionCardVm>(), _filter.ActiveTab);
        }

        if (!_isLoadingCatalog)
            ClearStatus();

        var dayTiers = _dayTier.Resolve();
        _currentRunDay = dayTiers.Day;
        var query = CollectionQuery.Run(
            _catalogCards,
            _filter,
            _facetAvailability,
            _sourceCatalog,
            _offerPoolCache,
            dayTiers.MaximumTier
        );

        // Publish first so a null (unavailable grid) skips normalization write-back.
        var projection = _grid.Publish(query.Cards, _filter.ActiveTab);
        if (projection == null)
            return null;

        AdoptNormalization(query.Normalization);
        return projection;
    }

    private void AdoptNormalization(CollectionFilterNormalization normalization)
    {
        if (normalization.ClearSelectedSource)
            _filter.ClearSelectedSource();
        if (normalization.RetainedTags != null)
        {
            _filter.Tags.Clear();
            _filter.Tags.UnionWith(normalization.RetainedTags);
        }
        if (normalization.RetainedKeywords != null)
        {
            _filter.Keywords.Clear();
            _filter.Keywords.UnionWith(normalization.RetainedKeywords);
        }
        if (normalization.RetainedMechanics != null)
        {
            _filter.Mechanics.Clear();
            _filter.Mechanics.UnionWith(normalization.RetainedMechanics);
        }
    }

    private CollectionPanelViewModel BuildModel(CollectionGridProjection projection)
    {
        var profile = CollectionTabProfile.For(_filter.ActiveTab);
        var availableTags = _facetAvailability.TagsFor(_filter.ActiveType);
        var availableKeywordOptions = _facetAvailability.KeywordOptionsFor(_filter.ActiveType);
        return new CollectionPanelViewModel
        {
            Title = CollectionPanelText.Title(),
            Subtitle = CollectionPanelText.Subtitle(),
            Supporters = _supporters,
            VisibleCount = projection.VisibleCount,
            StatusMessage = _statusVisible ? _statusMessage : null,
            IsLoading = _isLoadingCatalog,
            ActiveTab = _filter.ActiveTab,
            ActiveType = _filter.ActiveType,
            TabProfile = profile,
            SelectedHero = _filter.SelectedHero,
            AllHeroesSelected = _filter.AllHeroesSelected,
            SelectedTiers = _filter.Tiers,
            SelectedSizes = _filter.Sizes,
            SelectedTags = _filter.Tags,
            SelectedKeywords = _filter.Keywords,
            SelectedMechanics = _filter.Mechanics,
            TagMatchMode = _filter.TagMatchMode,
            KeywordMatchMode = _filter.KeywordMatchMode,
            SearchExpanded = _searchMode.IsExpanded,
            SearchQuery = _filter.SearchQuery,
            SelectedSourceKey = profile.ShowSourceFilter ? _filter.SelectedSourceKey : null,
            EncounteredMerchantSourceKeys = _encounteredMerchantSourceKeys,
            SourceSelectorEnabled = profile.ShowSourceFilter && !_isLoadingCatalog,
            SortPriority = _filter.SortPriority,
            DayFilterActive = _filter.UseRunDayFilter,
            DayFilterValue = _currentRunDay,
            AvailableHeroes = _availableHeroes,
            AvailableTiers = TierOrder,
            AvailableSizes = SizeOrder,
            AvailableTags = availableTags,
            AvailableKeywordOptions = availableKeywordOptions,
            AvailableSources = AvailableSourcesFor(_filter.ActiveTab),
            ContentHeight = projection.ContentHeight,
        };
    }

    private IReadOnlyList<CollectionSourceOptionViewModel> AvailableSourcesFor(
        CollectionTabKind activeTab
    )
    {
        var sourceKind = CollectionTabProfile.For(activeTab).SourceKind;
        if (!sourceKind.HasValue)
            return Array.Empty<CollectionSourceOptionViewModel>();

        var kind = sourceKind.Value;
        var effectiveHero = _filter.EffectiveHero;
        if (
            _availableSourcesCache is { } cached
            && cached.Kind == kind
            && cached.Hero == effectiveHero
        )
            return cached.Sources;

        var roster = CollectionSourceRoster.Build(_sourceCatalog.For(kind, effectiveHero));
        var result = new List<CollectionSourceOptionViewModel>(roster.Count);
        foreach (var item in roster)
        {
            var entry = item.Entry;
            result.Add(
                new CollectionSourceOptionViewModel
                {
                    SourceKey = entry.SourceKey,
                    DisplayName = entry.Name,
                    Description = entry.Description,
                    Kind = entry.Kind,
                    RepresentativeTemplateId = entry.PortraitTemplateId,
                }
            );
        }

        _availableSourcesCache = (kind, effectiveHero, result);
        return result;
    }

    private bool PruneInvisibleSourceSelections()
    {
        if (_catalogReadiness != CollectionCatalogReadiness.Accepted)
            return false;

        var effectiveHero = _filter.EffectiveHero;
        var sourceKind = CollectionTabProfile.For(_filter.ActiveTab).SourceKind;
        if (!sourceKind.HasValue)
            return _filter.ClearSelectedSource();

        var visibleSources = SourceKeysFor(sourceKind.Value, effectiveHero);
        return _filter.PruneSelectedSource(visibleSources);
    }

    private IReadOnlyList<string> SourceKeysFor(CollectionSourceKind kind, CardHero effectiveHero)
    {
        var keys = new List<string>();
        foreach (var entry in _sourceCatalog.For(kind, effectiveHero))
            keys.Add(entry.SourceKey);
        return keys;
    }

    private void NormalizeHeroSelectionWithoutSave()
    {
        var normalizedHero = CollectionHeroSelectionRoster.NormalizeSelection(
            _filter.SelectedHero,
            _catalogReadiness,
            _availableHeroes
        );
        if (_filter.SelectedHero != normalizedHero && _filter.SelectedHero.HasValue)
            _filter.ToggleHero(_filter.SelectedHero.Value);
    }

    private void SetCatalogCards(IReadOnlyList<CollectionCardVm> cards)
    {
        _catalogCards = cards;
        _facetAvailability = CollectionFacetAvailability.SnapshotFor(cards);
        CollectionCardNameOrder.Assign(cards);
        _availableSourcesCache = null;
    }

    private void SetCatalogState(
        CollectionCatalogReadiness readiness,
        IReadOnlyList<CollectionCardVm> cards
    )
    {
        _catalogReadiness = readiness;
        _availableHeroes = CollectionHeroSelectionRoster.ResolveAvailableHeroes(readiness, cards);
        SetCatalogCards(cards);
    }

    private void SetStatus(string message)
    {
        _statusMessage = message;
        _statusVisible = true;
    }

    private void ClearStatus()
    {
        _statusMessage = null;
        _statusVisible = false;
    }
}
