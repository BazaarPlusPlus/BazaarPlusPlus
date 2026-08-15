#nullable enable
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Game.CollectionPanel.Data;
using BazaarPlusPlus.Game.CollectionPanel.Sources;
using BazaarPlusPlus.Game.CollectionPanel.Ui;
using BazaarPlusPlus.Game.Supporters;
using BazaarPlusPlus.GameInterop.DayTiers;

namespace BazaarPlusPlus.Game.CollectionPanel;

// Single owner of Collection Panel presentable state: filter selections, search mode/debounce,
// catalog acceptance, and the derived render model. Commands and lifecycle events go in; a
// complete render outcome (view model plus scroll intents) comes out. Pure — no Unity types.
internal sealed class CollectionViewState
{
    private const float SearchRefreshDebounceSeconds = 0.16f;

    private static readonly ETier[] TierOrder =
    [
        ETier.Bronze,
        ETier.Silver,
        ETier.Gold,
        ETier.Diamond,
        ETier.Legendary,
    ];

    private static readonly ECardSize[] SizeOrder =
    [
        ECardSize.Small,
        ECardSize.Medium,
        ECardSize.Large,
    ];

    private readonly ICollectionGridPort _grid;
    private readonly ICollectionSourceCatalog _sourceCatalog;
    private readonly IGameDataDayTierResolver _dayTier;
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
    private IReadOnlyList<EHero> _availableHeroes =
        CollectionHeroSelectionRoster.BaseConcreteHeroes;

    private (
        CollectionSourceKind Kind,
        EHero Hero,
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
        IGameDataDayTierResolver dayTier,
        ICollectionPanelHeroPreferenceStore heroPreferenceStore
    )
    {
        _grid = grid ?? throw new ArgumentNullException(nameof(grid));
        _sourceCatalog = sourceCatalog ?? throw new ArgumentNullException(nameof(sourceCatalog));
        _dayTier = dayTier ?? throw new ArgumentNullException(nameof(dayTier));
        _heroPreferenceStore =
            heroPreferenceStore ?? throw new ArgumentNullException(nameof(heroPreferenceStore));
    }

    // Open-path probe surface only — not the filter. Preference load needs readiness + heroes
    // before ApplyOpenSelection runs.
    public CollectionCatalogReadiness CatalogReadiness => _catalogReadiness;

    public IReadOnlyList<EHero> AvailableHeroes => _availableHeroes;

    public int CatalogCardCount => _catalogCards.Count;

    public bool IsSearchRefreshPending => _searchRefreshGate.IsPending;

    public void PrepareCatalogForOpen(IReadOnlyList<CollectionCardVm>? cachedCards)
    {
        if (cachedCards != null)
            SetCatalogState(CollectionCatalogReadiness.Accepted, cachedCards);
        else
            SetCatalogState(CollectionCatalogReadiness.Loading, Array.Empty<CollectionCardVm>());
    }

    public CollectionRenderOutcome? SetActiveTab(CollectionTabKind tab)
    {
        if (!_filter.SelectTab(tab))
            return null;

        PruneInvisibleSourceSelections();
        return QueryAndRender(resetControlsScroll: true);
    }

    public CollectionRenderOutcome? ToggleHero(EHero hero)
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

    public CollectionRenderOutcome? ToggleTier(ETier tier)
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

    public CollectionRenderOutcome? ToggleSize(ECardSize size)
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

    public CollectionRenderOutcome? ToggleTag(ECardTag tag)
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

    public void ResetSearchForLifecycle()
    {
        _searchMode.Reset(_filter);
        _searchRefreshGate.Cancel();
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

    // Panel load coroutine was cancelled mid-flight; clear the loading chrome flag without
    // mutating catalog readiness (caller owns whether to ResetCatalog / reload).
    public void NoteCatalogLoadCancelled() => _isLoadingCatalog = false;

    public CollectionRenderOutcome ApplyOpenSelection(
        CollectionPanelSelectionState selection,
        IReadOnlyList<BPPSupporterSample> supporters,
        int? currentRunDay,
        IReadOnlyCollection<string>? encounteredMerchantSourceKeys = null
    )
    {
        if (selection == null)
            throw new ArgumentNullException(nameof(selection));
        if (supporters == null)
            throw new ArgumentNullException(nameof(supporters));

        _filter.ApplySelection(selection);
        if (_catalogReadiness == CollectionCatalogReadiness.Accepted)
            PruneInvisibleSourceSelections();

        _supporters = supporters;
        _encounteredMerchantSourceKeys.Clear();
        if (encounteredMerchantSourceKeys != null)
        {
            foreach (var sourceKey in encounteredMerchantSourceKeys)
                if (!string.IsNullOrWhiteSpace(sourceKey))
                    _encounteredMerchantSourceKeys.Add(sourceKey.Trim());
        }
        _currentRunDay = currentRunDay;
        return new CollectionRenderOutcome(
            BuildModel(_grid.Current),
            resetScroll: true,
            resetControlsScroll: true
        );
    }

    public CollectionRenderOutcome Rebuild() =>
        new(BuildModel(_grid.Current), resetScroll: false, resetControlsScroll: false);

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
            dayTiers.Table
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
        var dayFilterPresentation = CollectionDayFilterPresentation.For(
            profile,
            _filter.UseRunDayFilter
        );
        var heroFilterPresentation = CollectionHeroFilterPresentation.For(profile);
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
            HeroFilterVisible = heroFilterPresentation.IsVisible,
            HeroFilterEnabled = heroFilterPresentation.IsEnabled,
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
            DayFilterVisible = dayFilterPresentation.IsVisible,
            DayFilterEnabled = dayFilterPresentation.IsEnabled,
            DayFilterActive = dayFilterPresentation.IsActive,
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

    private IReadOnlyList<string> SourceKeysFor(CollectionSourceKind kind, EHero effectiveHero)
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
