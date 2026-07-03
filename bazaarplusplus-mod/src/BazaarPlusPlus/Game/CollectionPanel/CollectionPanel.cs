#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Core.Config;
using BazaarPlusPlus.Core.GameState;
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.Game.CollectionPanel.Data;
using BazaarPlusPlus.Game.CollectionPanel.Grid;
using BazaarPlusPlus.Game.CollectionPanel.Sources;
using BazaarPlusPlus.Game.CollectionPanel.Ui;
using BazaarPlusPlus.Game.OverlayPanels;
using BazaarPlusPlus.Game.Supporters;
using BazaarPlusPlus.GameInterop.TagTypography;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.UiTokens;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace BazaarPlusPlus.Game.CollectionPanel;

internal sealed class CollectionPanel : MonoBehaviour
{
    private const float CatalogBuildFrameBudgetMs = 4f;
    private const string OverlayPanelId = "CollectionPanel";
    private const int OverlaySortingBand = BppOverlaySorting.MainOverlayPanelBand;

    private static CollectionPanel? _instance;
    public static bool IsVisible => _instance != null && _instance._isVisible;
    internal static CollectionPanel? Instance => _instance;

    private static readonly EHero[] HeroOrder = new[]
    {
        EHero.Common,
        EHero.Vanessa,
        EHero.Dooley,
        EHero.Pygmalien,
        EHero.Karnok,
        EHero.Mak,
        EHero.Stelle,
        EHero.Jules,
    };

    private static readonly ETier[] TierOrder = new[]
    {
        ETier.Bronze,
        ETier.Silver,
        ETier.Gold,
        ETier.Diamond,
        ETier.Legendary,
    };

    private static readonly ECardSize[] SizeOrder = new[]
    {
        ECardSize.Small,
        ECardSize.Medium,
        ECardSize.Large,
    };

    private readonly CollectionCatalog _catalog = new();
    private readonly CollectionFilterState _filter = new();
    private readonly CollectionSourceOfferPoolCache _offerPoolCache = new();
    private readonly ICollectionPanelHeroPreferenceStore _heroPreferenceStore =
        new CollectionPanelHeroPreferenceStore();

    private IBppConfig _config = null!;
    private CollectionPanelView? _view;
    private CollectionGridOverlay? _overlay;
    private CollectionCardFactory? _factory;
    private CollectionGridVirtualizer? _virtualizer;

    private IBppServices _services = null!;
    private IReadOnlyList<CollectionCardVm> _catalogCards = Array.Empty<CollectionCardVm>();
    private CollectionFacetAvailabilitySnapshot _facetAvailability =
        CollectionFacetAvailabilitySnapshot.Empty;

    // Last AvailableSourcesFor projection. The source catalog is immutable after its one-time
    // load, so the roster only varies with (kind, selected hero).
    private (
        CollectionSourceKind Kind,
        EHero? Hero,
        IReadOnlyList<CollectionSourceOptionViewModel> Sources
    )? _availableSourcesCache;
    private IReadOnlyList<BPPSupporterSample> _supporters = Array.Empty<BPPSupporterSample>();
    private bool _isVisible;
    private bool _initialized;
    private string _lastSceneToken = string.Empty;
    private bool _statusVisible;
    private string? _statusMessage;
    private bool _viewportBoundsDirty;
    private Rect _viewportBoundsPx;
    private float _scrollY;
    private Coroutine? _loadCoroutine;
    private int _loadGeneration;
    private bool _isLoadingCatalog;

    // True when the last RefreshView ran before the game's async tooltip typography
    // registration completed: tag chips rendered degraded (string-table labels, no accent
    // color) and nothing else would re-render them without user interaction. Update polls for
    // the typography instance and re-refreshes once, so the startup window self-heals.
    private bool _viewMissedNativeTypography;

    // The run's current day, captured once per open in ResolveOpenSelection (null out of run, or
    // when Data.Run is unreadable). The Day toggle filters by this value, falling back to
    // DayTierSchedule.OutOfRunDay. Recomputed on every open.
    private int? _currentRunDay;

    public void Initialize(IBppServices services)
    {
        if (_initialized)
            return;
        _initialized = true;
        _instance = this;
        _services = services;
        _config = services.Config;
        _lastSceneToken = GetSceneToken(SceneManager.GetActiveScene());
        BppOverlayPanelMutex.Register(
            new BppOverlayPanelRegistration(
                OverlayPanelId,
                OverlaySortingBand,
                () => _instance?._isVisible == true,
                () => _instance?.Close()
            )
        );
    }

    internal static void NotifyLocaleChanged()
    {
        if (_instance == null)
            return;
        _instance.InvalidateCatalog("locale-change");
        if (_instance._isVisible)
            _instance.StartPanelLoad();
    }

    internal static void OpenFromDockButton()
    {
        if (_instance == null)
        {
            BppLog.Warn("CollectionPanel", "Dock button requested before CollectionPanel mounted.");
            return;
        }
        _instance.Open(_instance.ResolveOpenSelection());
    }

    internal static CollectionPanelSelectionState GetCurrentSelectionState() =>
        _instance?._filter.ToSelectionState() ?? CollectionPanelSelectionState.Default;

    private void Open() => Open(ResolveOpenSelection());

    private CollectionPanelSelectionState ResolveOpenSelection()
    {
        var isInGameRun = IsInGameRunForOpen();
        var rememberedHero = isInGameRun ? null : _heroPreferenceStore.Load();
        var hero = isInGameRun ? TryReadCurrentHero() : null;
        var encounterIds = isInGameRun ? TryReadEncounterIds() : EncounterIdsSnapshot.Empty;
        var selection = CollectionPanelOpenSelectionResolver.Resolve(
            isInGameRun,
            hero,
            encounterIds.CurrentEncounterTemplateId,
            encounterIds.ChoiceSelectionTemplateIds,
            CollectionSourceCatalog.Entries,
            rememberedHero
        );

        BppLog.Debug(
            "CollectionPanel",
            "Open selection resolved "
                + $"inRun={isInGameRun} "
                + $"hero={selection.SelectedHero?.ToString() ?? "none"} "
                + $"rememberedHero={rememberedHero?.ToString() ?? "none"} "
                + $"currentEncounterId={encounterIds.CurrentEncounterId ?? "none"} "
                + $"currentEncounterTemplateId={encounterIds.CurrentEncounterTemplateId?.ToString() ?? "none"} "
                + $"sourceKind={selection.SelectedSourceKind} "
                + $"source={selection.SelectedSourceKey ?? "none"} "
                + $"matched={IsMatchedOpenSelection(selection)}"
        );

        // The day does not change while the panel is open, so capture it once here. Both Open()
        // entry paths call ResolveOpenSelection before applying the selection.
        _currentRunDay = isInGameRun ? TryReadCurrentDay() : null;
        return selection;
    }

    private static bool IsMatchedOpenSelection(CollectionPanelSelectionState selection) =>
        selection.SelectedSourceKind != CollectionSourceKind.Merchant
        || !string.Equals(
            selection.SelectedSourceKey,
            CollectionPanelSelectionState.DefaultMerchantSourceKey,
            StringComparison.Ordinal
        );

    private bool IsInGameRunForOpen()
    {
        try
        {
            return _services.RunContext.IsInGameRun
                || _services.GameStateProbe.ComputeIsInGameRun();
        }
        catch (Exception ex)
        {
            BppLog.Warn("CollectionPanel", $"Open selection run-state read failed: {ex.Message}");
            return _services.RunContext.IsInGameRun;
        }
    }

    private static EHero? TryReadCurrentHero()
    {
        try
        {
            var runHero = TheBazaar.Data.Run?.Player?.Hero;
            if (CollectionPanelOpenSelectionResolver.IsConcreteHero(runHero))
                return runHero;

            var selectedHero = TheBazaar.Data.SelectedHero;
            return CollectionPanelOpenSelectionResolver.IsConcreteHero(selectedHero)
                ? selectedHero
                : null;
        }
        catch (Exception ex)
        {
            BppLog.Warn("CollectionPanel", $"Open selection hero read failed: {ex.Message}");
            return null;
        }
    }

    private static int? TryReadCurrentDay()
    {
        try
        {
            return (int?)TheBazaar.Data.Run?.Day;
        }
        catch (Exception ex)
        {
            BppLog.Warn("CollectionPanel", $"Open selection day read failed: {ex.Message}");
            return null;
        }
    }

    private EncounterIdsSnapshot TryReadEncounterIds()
    {
        try
        {
            return _services.EncounterState.GetEncounterIds();
        }
        catch (Exception ex)
        {
            BppLog.Warn("CollectionPanel", $"Open selection encounter read failed: {ex.Message}");
            return EncounterIdsSnapshot.Empty;
        }
    }

    private void Open(CollectionPanelSelectionState selection)
    {
        if (TheBazaar.Data.IsInCombat)
        {
            BppLog.Info("CollectionPanel", "Open suppressed: combat is active.");
            return;
        }

        BppOverlayPanelMutex.CloseOthers(OverlayPanelId, OverlaySortingBand);

        ApplyOpenSelection(selection);
        // Temporary main-path probe: EnsureView() is heavy one-time UITK construction (visual
        // tree + CJK glyph raster + cold OTF extract) that runs on the click frame BEFORE the
        // panel is shown and is invisible to CollectionPanelLoadDiagnostics (created later in the
        // coroutine). Time the first construction so its click-frame cost is attributable.
        var ensureViewStartedAt = System.Diagnostics.Stopwatch.GetTimestamp();
        var firstViewConstruction = _view == null;
        EnsureView();
        if (firstViewConstruction)
        {
            var ensureViewMs =
                (System.Diagnostics.Stopwatch.GetTimestamp() - ensureViewStartedAt)
                * 1000.0
                / System.Diagnostics.Stopwatch.Frequency;
            BppLog.Info(
                "CollectionPanelLoad",
                "openPrologue ensureView="
                    + ensureViewMs.ToString(
                        "0.0",
                        System.Globalization.CultureInfo.InvariantCulture
                    )
                    + "ms (first view construction)"
            );
        }
        _supporters = BPPSupporters.SampleMany(4);
        _isVisible = true;
        // SetVisible starts the fade-in ramp; overlay activates so its CanvasGroup starts
        // mirroring the view's opacity (Update pushes the live value each frame).
        _view!.SetVisible(true);
        _view.ResetControlsScroll();
        _overlay?.SetVisible(true);
        _overlay?.SetAlpha(_view!.CurrentOpacity);
        StartPanelLoad();
    }

    private void ApplyOpenSelection(CollectionPanelSelectionState selection)
    {
        _filter.ApplySelection(selection);
        // The Day toggle's on/off persists across opens (like the package toggle); when it is on,
        // re-pin it to the freshly-read day. _currentRunDay was just captured in
        // ResolveOpenSelection, which always runs before this.
        if (_filter.SelectedRunDay != null)
            _filter.SelectedRunDay = _currentRunDay ?? DayTierSchedule.OutOfRunDay;
        PruneInvisibleSourceSelections();
        _scrollY = 0f;
    }

    private void Close()
    {
        if (!_isVisible)
            return;
        CancelPanelLoad();
        _isVisible = false;
        HideNativeCardLayerImmediately();
        // Let the UITK chrome fade out, but do not keep the native card overlay alive during
        // that animation. CardPreviewBase instances live on a sibling ScreenSpaceOverlay canvas,
        // so delaying its teardown leaves visible card art after the panel has been dismissed.
        _view?.SetVisible(false);
    }

    private void Update()
    {
        DetectSceneChange();

        // Opportunistically warm the card catalog off-thread once static data is ready, so the
        // first panel open does not pay the full-table card read (JsonGameDataManager.GetCardMap
        // -> ReadAllCards) on the main thread. The catalog kicks a single shared Task per
        // static-data source; the first open then awaits it instead of blocking.
        if (!_catalog.HasCardMapLoadStarted)
            _catalog.BeginCardMapLoad(out _);

        if (_isVisible && TheBazaar.Data.IsInCombat)
        {
            Close();
            return;
        }

        var keyboard = Keyboard.current;
        if (keyboard != null && IsPlainTabPressed(keyboard))
        {
            ToggleFromHotkey();
            return;
        }

        var dt = Time.unscaledDeltaTime;

        // Drive the panel fade every frame regardless of _isVisible so a Close mid-frame
        // can finish its fade-out animation before we tear runtime down.
        _view?.TickOpacity(dt);
        _view?.TickLoading(dt);
        if (_view != null && _overlay != null)
            _overlay.SetAlpha(_view.CurrentOpacity);

        if (!_isVisible)
        {
            // Close() already hides the native card layer synchronously. Keep this as an
            // idempotent cleanup fallback once the remaining UITK fade-out has settled.
            if (_view != null && !_view.IsFadingOrVisible)
            {
                _overlay?.SetVisible(false);
                _virtualizer?.Dispose();
            }
            return;
        }

        if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
        {
            Close();
            return;
        }

        // Startup self-heal for tag typography: a refresh that ran inside the game's async
        // typography registration window rendered degraded chips, and no further Refresh
        // arrives without user interaction. Re-render once the instance appears (the check is
        // a static null probe per frame; RefreshView clears the flag).
        if (_viewMissedNativeTypography && NativeTagTypography.IsNativeTypographyAvailable)
            RefreshView();

        if (_viewportBoundsDirty && _virtualizer != null && _overlay != null)
        {
            _viewportBoundsDirty = false;
            _overlay.SetPosition(_viewportBoundsPx.position);
            _overlay.SetClipSize(_viewportBoundsPx.size);
            _virtualizer.SetViewport(_viewportBoundsPx.width, _viewportBoundsPx.height);
            // The base unit (and therefore ContentHeight) is derived from the viewport width,
            // so re-publish the scroll-spacer height once real bounds arrive — otherwise the
            // first-open estimate computed at the placeholder width leaves the bottom rows
            // unreachable.
            _view?.UpdateContentSpacerHeight(_virtualizer.ContentHeight);
        }

        // UITK's default wheel handler updates scrollOffset directly; we just read the
        // resulting position each frame and feed it to the virtualizer. (An earlier smooth-
        // wheel intercept lived here but killed wheel scrolling entirely — see §16.6.)
        if (_view != null)
            _scrollY = _view.ReadScrollYPixels();
        _virtualizer?.SetScrollY(_scrollY);
        _virtualizer?.Tick();
        _virtualizer?.TickFades(dt);

        if (CollectionGridConstants.UsePolledHover && _virtualizer != null)
        {
            var mouse = Mouse.current;
            if (mouse != null)
            {
                var pos = mouse.position.ReadValue();
                _virtualizer.PollHover(pos, _viewportBoundsPx);
            }
        }
    }

    private void HideNativeCardLayerImmediately()
    {
        _virtualizer?.Dispose();
        _overlay?.SetAlpha(0f);
        _overlay?.SetVisible(false);
    }

    private static bool IsPlainTabPressed(Keyboard keyboard) =>
        keyboard.tabKey.wasPressedThisFrame
        && keyboard.ctrlKey.isPressed == false
        && keyboard.altKey.isPressed == false
        && keyboard.shiftKey.isPressed == false;

    private void ToggleFromHotkey()
    {
        if (_isVisible)
            Close();
        else
            Open();
    }

    private void DetectSceneChange()
    {
        var token = GetSceneToken(SceneManager.GetActiveScene());
        if (string.Equals(token, _lastSceneToken, StringComparison.Ordinal))
            return;
        _lastSceneToken = token;
        if (_isVisible)
            Close();
        DisposeUnityRuntime();
    }

    private void OnDestroy()
    {
        if (ReferenceEquals(_instance, this))
            _instance = null;
        BppOverlayPanelMutex.Unregister(OverlayPanelId);
        DisposeRuntime();
    }

    private void DisposeRuntime()
    {
        DisposeUnityRuntime();
        InvalidateCatalog("runtime-dispose");
    }

    private void DisposeUnityRuntime()
    {
        CancelPanelLoad();
        _virtualizer?.Dispose();
        _virtualizer = null;
        _factory?.DestroyAll();
        _factory = null;
        _overlay?.Dispose();
        _overlay = null;
        _view?.Dispose();
        _view = null;
    }

    private void EnsureView()
    {
        if (_view != null)
            return;

        _view = new CollectionPanelView(transform, new PanelCommands(this));

        _view.GridViewportBoundsChanged += bounds =>
        {
            _viewportBoundsPx = bounds;
            _viewportBoundsDirty = true;
        };

        _view.EnsureCreated();

        _overlay = new CollectionGridOverlay();
        _overlay.EnsureInitialized();

        _factory = new CollectionCardFactory(
            _overlay.BoardRoot!,
            CollectionGridOverlay.DefaultLayer
        );
        _virtualizer = new CollectionGridVirtualizer(_overlay, _factory);
    }

    private static CollectionFacetMatchMode ToggleMatchMode(CollectionFacetMatchMode mode) =>
        mode == CollectionFacetMatchMode.All
            ? CollectionFacetMatchMode.Any
            : CollectionFacetMatchMode.All;

    private sealed class PanelCommands(CollectionPanel panel) : ICollectionPanelCommands
    {
        public void Close() => panel.Close();

        public void SetActiveType(ECardType type)
        {
            if (!panel._filter.SelectActiveType(type))
                return;
            panel.PruneInvisibleSourceSelections();
            panel._scrollY = 0f;
            panel.ApplyFilters();
            panel.RefreshView();
            panel._view?.ResetControlsScroll();
        }

        public void ToggleHero(EHero hero)
        {
            panel._filter.ToggleHero(hero);
            panel._heroPreferenceStore.Save(hero);
            panel.PruneInvisibleSourceSelections();
            panel._scrollY = 0f;
            panel.ApplyFilters();
            panel.RefreshView();
            panel._view?.ResetControlsScroll();
        }

        public void ToggleTier(ETier tier)
        {
            if (!panel._filter.Tiers.Remove(tier))
                panel._filter.Tiers.Add(tier);
            panel._scrollY = 0f;
            panel.ApplyFilters();
            panel.RefreshView();
            panel._view?.ResetControlsScroll();
        }

        public void ToggleRunDayFilter()
        {
            // Toggle whether the day participates in filtering. On uses the current run day
            // (or OutOfRunDay out of run); off clears the day filter entirely.
            panel._filter.SelectedRunDay = panel._filter.SelectedRunDay is null
                ? panel._currentRunDay ?? DayTierSchedule.OutOfRunDay
                : (int?)null;
            panel._scrollY = 0f;
            panel.ApplyFilters();
            panel.RefreshView();
        }

        public void ToggleSize(ECardSize size)
        {
            if (!panel._filter.Sizes.Remove(size))
                panel._filter.Sizes.Add(size);
            panel._scrollY = 0f;
            panel.ApplyFilters();
            panel.RefreshView();
        }

        public void ToggleTag(ECardTag tag)
        {
            if (!panel._filter.Tags.Remove(tag))
                panel._filter.Tags.Add(tag);
            panel._scrollY = 0f;
            panel.ApplyFilters();
            panel.RefreshView();
        }

        public void ToggleKeyword(EHiddenTag keyword)
        {
            if (!panel._filter.Keywords.Remove(keyword))
                panel._filter.Keywords.Add(keyword);
            panel._scrollY = 0f;
            panel.ApplyFilters();
            panel.RefreshView();
        }

        public void ToggleTagMatchMode()
        {
            panel._filter.TagMatchMode = ToggleMatchMode(panel._filter.TagMatchMode);
            panel._scrollY = 0f;
            panel.ApplyFilters();
            panel.RefreshView();
        }

        public void ToggleKeywordMatchMode()
        {
            panel._filter.KeywordMatchMode = ToggleMatchMode(panel._filter.KeywordMatchMode);
            panel._scrollY = 0f;
            panel.ApplyFilters();
            panel.RefreshView();
        }

        public void ToggleSource(string sourceKey)
        {
            panel._filter.ToggleSource(panel._filter.ActiveType, sourceKey);
            panel._scrollY = 0f;
            panel.ApplyFilters();
            panel.RefreshView();
        }

        public void TogglePackagesOnly()
        {
            if (!panel._filter.SelectPackagesOnly())
                return;

            panel.PruneInvisibleSourceSelections();
            panel._scrollY = 0f;
            panel.ApplyFilters();
            panel.RefreshView();
        }

        public void SetSortPriority(CollectionSortPriority priority)
        {
            if (panel._filter.SortPriority == priority)
                return;
            panel._filter.SortPriority = priority;
            panel._scrollY = 0f;
            panel.ApplyFilters();
            panel.RefreshView();
        }
    }

    private void StartPanelLoad()
    {
        CancelPanelLoad();
        var generation = ++_loadGeneration;
        _loadCoroutine = StartCoroutine(LoadPanelAsync(generation));
    }

    private void CancelPanelLoad()
    {
        _loadGeneration++;
        if (_loadCoroutine != null)
        {
            StopCoroutine(_loadCoroutine);
            _loadCoroutine = null;
        }
        _isLoadingCatalog = false;
    }

    private IEnumerator LoadPanelAsync(int generation)
    {
        var diagnostics = new CollectionPanelLoadDiagnostics();
        _isLoadingCatalog = true;
        SetStatus(CollectionPanelText.CatalogLoading());
        ApplyEmptyVisibleSet();
        RefreshView();

        yield return null;

        if (!IsLoadGenerationCurrent(generation))
            yield break;

        var started = diagnostics.Now();
        CollectionCatalogBuildResult? catalogResult = null;
        if (_catalog.TryGetCached(out var cached))
        {
            catalogResult = cached;
            SetCatalogCards(cached.Cards);
            ClearStatus();
        }
        else
        {
            // First open (cache miss): acquire the full card map OFF the main thread. The heavy
            // cost is JsonGameDataManager.GetCardMap() -> ReadAllCards (~22 MB full-table SQLite
            // read + polymorphic deserialize); running it synchronously here froze the click path
            // because it sits before the time-sliced Step loop and cannot be preempted. Await the
            // shared prewarm Task (kicked from Update / reused here) while the loading shell +
            // spinner keep animating, then build the catalog from the materialised map.
            var acquireStarted = diagnostics.Now();
            var loadTask = _catalog.BeginCardMapLoad(out var source);
            if (loadTask != null)
            {
                while (!loadTask.IsCompleted)
                {
                    if (!IsLoadGenerationCurrent(generation))
                        yield break;
                    yield return null;
                }
            }
            diagnostics.AddSegment("catalogAcquire", acquireStarted);

            var map = loadTask is { Status: TaskStatus.RanToCompletion } ? loadTask.Result : null;
            if (loadTask is { IsFaulted: true })
            {
                // A faulted Task always carries a non-null AggregateException.
                BppLog.Error(
                    "CollectionPanel",
                    "Off-thread card map load failed.",
                    loadTask.Exception!.GetBaseException()
                );
            }

            if (
                _catalog.TryCreateBuildSession(
                    source,
                    map,
                    out var session,
                    out var unavailableReason
                )
            )
            {
                var buildSession = session!;
                using (buildSession)
                {
                    while (true)
                    {
                        var frameStartedAt = System.Diagnostics.Stopwatch.GetTimestamp();
                        if (buildSession.Step(() => ShouldPauseCatalogBuild(frameStartedAt)))
                            break;
                        if (!IsLoadGenerationCurrent(generation))
                            yield break;
                        yield return null;
                    }

                    catalogResult = _catalog.Commit(buildSession);
                    SetCatalogCards(catalogResult.Cards);
                    ClearStatus();
                }
            }
            else
            {
                SetCatalogCards(Array.Empty<CollectionCardVm>());
                SetStatus(CollectionPanelText.CatalogUnavailable());
                diagnostics.AddValue("unavailableReason", unavailableReason);
            }
        }
        diagnostics.AddSegment("catalog", started);
        if (catalogResult != null)
        {
            diagnostics.AddValue("catalogCacheHit", catalogResult.WasCacheHit ? "true" : "false");
            diagnostics.AddValue("sourceTemplates", catalogResult.SourceTemplateCount);
            diagnostics.AddValue("accepted", catalogResult.AcceptedCount);
            diagnostics.AddValue("rejected", catalogResult.RejectedCount);
        }

        if (!IsLoadGenerationCurrent(generation))
            yield break;

        started = diagnostics.Now();
        ApplyFilters();
        diagnostics.AddSegment("filter", started);

        started = diagnostics.Now();
        _isLoadingCatalog = false;
        if (_catalogCards.Count > 0)
            ClearStatus();
        RefreshView();
        diagnostics.AddSegment("refresh", started);
        diagnostics.AddValue("catalogCards", _catalogCards.Count);
        diagnostics.AddValue("visibleCards", _virtualizer?.VisibleCount ?? 0);
        diagnostics.Log(_catalogCards.Count > 0 ? "loaded" : "unavailable");
        _loadCoroutine = null;
    }

    private bool IsLoadGenerationCurrent(int generation) =>
        generation == _loadGeneration && _isVisible;

    private static bool ShouldPauseCatalogBuild(long startedAt)
    {
        var elapsedMs =
            (System.Diagnostics.Stopwatch.GetTimestamp() - startedAt)
            * 1000.0
            / System.Diagnostics.Stopwatch.Frequency;
        return elapsedMs >= CatalogBuildFrameBudgetMs;
    }

    private void ApplyEmptyVisibleSet()
    {
        if (_virtualizer == null)
            return;

        _virtualizer.SetVisible(Array.Empty<CollectionCardVm>(), _filter.ActiveType);
        _view?.ResetScroll();
        _scrollY = 0f;
    }

    private void ApplyFilters()
    {
        if (_virtualizer == null)
            return;
        if (_catalogCards.Count == 0)
        {
            _virtualizer.SetVisible(Array.Empty<CollectionCardVm>(), _filter.ActiveType);
        }
        else
        {
            TrimUnavailableFacetSelections();
            var sourceEntry = _filter.PackagesOnly ? null : ResolveSelectedSourceEntry();
            var hasSelectedSource = sourceEntry != null;
            IReadOnlyCollection<Guid>? offeredCardIds = null;
            IReadOnlyDictionary<
                Guid,
                IReadOnlyList<CollectionSourceOfferMatch>
            >? offerMatchesByCardId = null;
            if (sourceEntry != null)
            {
                var offerPoolResult = _offerPoolCache.GetOrResolve(
                    sourceEntry,
                    _filter.SelectedHero,
                    _catalogCards
                );
                if (offerPoolResult.Status == CollectionSourceOfferPoolStatus.Ready)
                {
                    offeredCardIds = offerPoolResult.OfferedCardIds;
                    offerMatchesByCardId = offerPoolResult.OfferMatchesByCardId;
                }
            }

            if (!_isLoadingCatalog)
                ClearStatus();
            var ordered = CollectionFilterEngine.Apply(
                _catalogCards,
                _filter,
                new CollectionFilterContext
                {
                    OfferedCardIds = offeredCardIds,
                    ApplyHeroFilter =
                        !_filter.PackagesOnly
                        && (!hasSelectedSource || _filter.ActiveType == ECardType.Skill),
                    // A source whose offer rule pins a starting tier deals that tier on any
                    // day; only exempt once its pool is actually narrowing the result.
                    SuppressDayGate =
                        !_filter.PackagesOnly
                        && offeredCardIds != null
                        && sourceEntry!.SuppressDayGate,
                }
            );
            _virtualizer.SetVisible(ordered, _filter.ActiveType, offerMatchesByCardId);
        }
        ResetVisibleScroll();
    }

    private void RefreshView()
    {
        if (_view == null || _virtualizer == null)
            return;

        var profile = CollectionTabProfile.For(_filter.ActiveType);
        var availableTags = _facetAvailability.ItemTags;
        var availableKeywords = _facetAvailability.KeywordsFor(_filter.ActiveType);
        var model = new CollectionPanelViewModel
        {
            Title = CollectionPanelText.Title(),
            Subtitle = CollectionPanelText.Subtitle(),
            Supporters = _supporters,
            CountText = CollectionPanelText.MatchCount(_virtualizer.VisibleCount),
            StatusMessage = _statusVisible ? _statusMessage : null,
            IsLoading = _isLoadingCatalog,
            ActiveType = _filter.ActiveType,
            TabProfile = profile,
            // The view only does Contains lookups on these inside the synchronous Refresh and
            // never retains the model, so the live filter sets are shared instead of copied.
            SelectedHeroes = _filter.Heroes,
            SelectedTiers = _filter.Tiers,
            SelectedSizes = _filter.Sizes,
            SelectedTags = _filter.Tags,
            SelectedKeywords = _filter.Keywords,
            TagMatchMode = _filter.TagMatchMode,
            KeywordMatchMode = _filter.KeywordMatchMode,
            SelectedSourceKey = _filter.PackagesOnly ? null : _filter.SelectedSourceKey,
            PackagesOnly = _filter.PackagesOnly,
            SourceSelectorEnabled = !_isLoadingCatalog && !_filter.PackagesOnly,
            SortPriority = _filter.SortPriority,
            DayFilterActive = _filter.SelectedRunDay != null,
            DayFilterValue = _currentRunDay ?? DayTierSchedule.OutOfRunDay,
            AvailableHeroes = HeroOrder,
            AvailableTiers = TierOrder,
            AvailableSizes = SizeOrder,
            AvailableTags = availableTags,
            AvailableKeywords = availableKeywords,
            AvailableSources = AvailableSourcesFor(_filter.ActiveType),
            ContentHeight = _virtualizer.ContentHeight,
        };
        // Record whether this render has native typography; while it does not, Update polls for
        // the late async registration and re-refreshes so the degraded chips self-heal. Written
        // BEFORE the render: typography registration is a main-thread continuation that cannot
        // interleave with the synchronous Refresh below, so the value is identical either way,
        // and writing first keeps a throwing Refresh from leaving the flag armed (which would
        // turn a one-shot failure into a per-frame retry).
        _viewMissedNativeTypography = !NativeTagTypography.IsNativeTypographyAvailable;
        _view.Refresh(model);
    }

    private void TrimUnavailableFacetSelections()
    {
        var profile = CollectionTabProfile.For(_filter.ActiveType);
        // ShowTagFilter is true only for the Item profile, so ItemTags matches what
        // TagsFor(_catalogCards, _filter.ActiveType) would compute here.
        if (profile.ShowTagFilter)
            TrimSet(_filter.Tags, _facetAvailability.ItemTags);
        if (profile.ShowKeywordFilter)
            TrimSet(_filter.Keywords, _facetAvailability.KeywordsFor(_filter.ActiveType));
    }

    private static void TrimSet<T>(HashSet<T> selected, IReadOnlyList<T> available)
    {
        if (selected.Count == 0)
            return;
        var allowed = available as HashSet<T> ?? new HashSet<T>(available);
        selected.RemoveWhere(value => !allowed.Contains(value));
    }

    private IReadOnlyList<CollectionSourceOptionViewModel> AvailableSourcesFor(ECardType activeType)
    {
        var kind = CollectionTabProfile.For(activeType).SourceKind;
        var selectedHero = _filter.SelectedHero;
        if (
            _availableSourcesCache is { } cached
            && cached.Kind == kind
            && cached.Hero == selectedHero
        )
            return cached.Sources;

        var roster = CollectionSourceRoster.Build(CollectionSourceCatalog.For(kind, selectedHero));
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
        _availableSourcesCache = (kind, selectedHero, result);
        return result;
    }

    private bool PruneInvisibleSourceSelections()
    {
        var selectedHero = _filter.SelectedHero;
        var visibleSources = SourceKeysFor(
            CollectionTabProfile.For(_filter.ActiveType).SourceKind,
            selectedHero
        );
        return _filter.PruneSelectedSource(visibleSources);
    }

    private static IReadOnlyList<string> SourceKeysFor(
        CollectionSourceKind kind,
        EHero? selectedHero
    )
    {
        var keys = new List<string>();
        foreach (var entry in CollectionSourceCatalog.For(kind, selectedHero))
            keys.Add(entry.SourceKey);
        return keys;
    }

    private CollectionSourceEntry? ResolveSelectedSourceEntry()
    {
        var sourceKey = _filter.SelectedSourceKey;
        if (string.IsNullOrWhiteSpace(sourceKey))
            return null;

        if (!CollectionSourceCatalog.TryGetBySourceKey(sourceKey!, out var entry) || entry == null)
        {
            _filter.ClearSelectedSource();
            return null;
        }

        var expectedKind = CollectionTabProfile.For(_filter.ActiveType).SourceKind;
        if (entry.Kind != expectedKind)
        {
            _filter.ClearSelectedSource();
            return null;
        }

        var selectedHero = _filter.SelectedHero;
        if (selectedHero.HasValue && !entry.AppliesToHero(selectedHero.Value))
        {
            _filter.ClearSelectedSource();
            return null;
        }

        return entry;
    }

    private void ResetVisibleScroll()
    {
        // Reset the actual ScrollView scrollOffset and cancel any in-flight smooth-wheel
        // animation, otherwise switching from a large set to a small one strands the user
        // at the bottom (scrollOffset clamps to the new max instead of returning to top).
        _view?.ResetScroll();
        _scrollY = 0f;
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

    private void InvalidateCatalog(string reason)
    {
        SetCatalogCards(Array.Empty<CollectionCardVm>());
        _offerPoolCache.Clear();
        _catalog.InvalidateCache(reason);
    }

    // Facet availability is a pure projection of the immutable catalog, so it is recomputed
    // only here — at the points where the catalog itself changes — never per RefreshView.
    private void SetCatalogCards(IReadOnlyList<CollectionCardVm> cards)
    {
        _catalogCards = cards;
        _facetAvailability = CollectionFacetAvailability.SnapshotFor(cards);
    }

    private static string GetSceneToken(Scene scene) =>
        $"{scene.name}|{scene.path}|{scene.buildIndex}|{scene.isLoaded}";
}
