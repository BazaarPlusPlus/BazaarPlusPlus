#nullable enable
using System.Collections;
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Core.Config;
using BazaarPlusPlus.Core.GameState;
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.Game.CollectionPanel.Data;
using BazaarPlusPlus.Game.CollectionPanel.Grid;
using BazaarPlusPlus.Game.CollectionPanel.Sources;
using BazaarPlusPlus.Game.CollectionPanel.Ui;
using BazaarPlusPlus.Game.Encounters;
using BazaarPlusPlus.Game.Input;
using BazaarPlusPlus.Game.OverlayPanels;
using BazaarPlusPlus.Game.Supporters;
using BazaarPlusPlus.GameInterop.CardPreview;
using BazaarPlusPlus.GameInterop.StaticCards;
using BazaarPlusPlus.GameInterop.TagTypography;
using BazaarPlusPlus.Infrastructure;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace BazaarPlusPlus.Game.CollectionPanel;

internal sealed class CollectionPanel : MonoBehaviour
{
    private const float CatalogBuildFrameBudgetMs = 4f;
    private const float SearchRefreshDebounceSeconds = 0.16f;
    private const string OverlayPanelId = "CollectionPanel";

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

    private CollectionCatalog _catalog = null!;
    private readonly CollectionFilterState _filter = new();
    private readonly CollectionSearchRefreshGate _searchRefreshGate = new(
        SearchRefreshDebounceSeconds
    );
    private Keyboard? _imeKeyboard;
    private bool _isImeComposing;
    private readonly CollectionSourceOfferPoolCache _offerPoolCache = new();
    private readonly ICollectionSourceCatalog _sourceCatalog = new StaticCollectionSourceCatalog();
    private readonly ICollectionPanelHeroPreferenceStore _heroPreferenceStore =
        new CollectionPanelHeroPreferenceStore();
    private readonly CollectionPanelSelectionLogState _selectionLogState = new();

    private IBppConfig _config = null!;
    private INativeCardPreviewHost _nativeCardPreviewHost = null!;
    private CollectionPanelView? _view;
    private CollectionGridOverlay? _overlay;
    private INativeCardPreviewScope? _previewScope;
    private CollectionGridVirtualizer? _virtualizer;
    private CollectionCardArtCache? _artCache;
    private CollectionCardMaterialCache? _materialCache;
    private CollectionCardCacheSession? _cacheSession;

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
    private IOverlayPanelHandle? _overlayHandle;
    private bool _isVisible;
    private bool _initialized;
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

    public void Initialize(
        IBppServices services,
        BppStaticCardMapProvider cardMapProvider,
        INativeCardPreviewHost nativeCardPreviewHost
    )
    {
        if (_initialized)
            return;
        if (services == null)
            throw new ArgumentNullException(nameof(services));
        if (cardMapProvider == null)
            throw new ArgumentNullException(nameof(cardMapProvider));
        if (nativeCardPreviewHost == null)
            throw new ArgumentNullException(nameof(nativeCardPreviewHost));

        _initialized = true;
        _instance = this;
        _services = services;
        _config = services.Config;
        _nativeCardPreviewHost = nativeCardPreviewHost;
        _catalog = new CollectionCatalog(cardMapProvider);
    }

    internal void AttachToOverlayHost(OverlayPanelHost overlayHost)
    {
        if (_overlayHandle != null)
            return;

        _overlayHandle = overlayHost.Register(
            new OverlayPanelRegistration(
                OverlayPanelId,
                BppHotkeyActionId.ToggleCollectionPanel,
                onOpen: Open,
                onClose: Close,
                tick: Tick
            )
            {
                OnSceneChanged = DisposeUnityRuntime,
                HotkeyGuard = () => !IsVisible || !IsTextInputFocused(),
            }
        );
    }

    private bool IsTextInputFocused() => _view?.IsTextInputFocused() == true;

    internal static void NotifyLocaleChanged()
    {
        if (_instance == null)
            return;
        _instance.InvalidateCatalog(CollectionPanelLogReasonCode.LocaleChange);
        if (_instance._isVisible)
            _instance.StartPanelLoad();
    }

    internal static void OpenFromDockButton()
    {
        if (_instance?._overlayHandle == null)
        {
            BppLog.ErrorEvent(
                CollectionPanelLogEvents.OpenFailed,
                CollectionPanelLogEvents.OpenFailedReasonCode.Bind(
                    CollectionPanelLogReasonCode.NotMounted
                )
            );
            return;
        }

        var outcome = _instance._overlayHandle.RequestOpen();
        switch (outcome)
        {
            case OverlayRequestOutcome.Executed:
            case OverlayRequestOutcome.AlreadyInState:
                return;
            case OverlayRequestOutcome.SuppressedByCombat:
                BppLog.DebugEvent(
                    CollectionPanelLogEvents.OpenSkipped,
                    static () =>
                        [
                            CollectionPanelLogEvents.OpenSkippedReasonCode.Bind(
                                CollectionPanelLogReasonCode.CombatActive
                            ),
                        ]
                );
                return;
            case OverlayRequestOutcome.UnknownPanel:
            default:
                BppLog.ErrorEvent(
                    CollectionPanelLogEvents.OpenFailed,
                    CollectionPanelLogEvents.OpenFailedReasonCode.Bind(
                        CollectionPanelLogReasonCode.UnknownPanel
                    )
                );
                return;
        }
    }

    private void Open() => Open(ResolveOpenSelection());

    private CollectionPanelSelectionState ResolveOpenSelection()
    {
        var failures = new List<CollectionPanelSelectionProbeFailure>(4);
        var isInGameRun = TryReadIsInGameRunForOpen(failures);
        var rememberedHero = isInGameRun ? null : _heroPreferenceStore.Load();
        var hero = isInGameRun ? TryReadCurrentHero(failures) : null;
        var encounterIds = isInGameRun ? TryReadEncounterIds(failures) : EncounterIdsSnapshot.Empty;
        _currentRunDay = isInGameRun ? TryReadCurrentDay(failures) : null;
        var selection = CollectionPanelOpenSelectionResolver.Resolve(
            isInGameRun,
            hero,
            encounterIds.CurrentEncounterTemplateId,
            encounterIds.ChoiceSelectionTemplateIds,
            CollectionSourceCatalog.Entries,
            rememberedHero
        );

        _selectionLogState.ObserveOpen(
            failures.Count == 0
                ? CollectionPanelSelectionOpenObservation.Complete()
                : CollectionPanelSelectionOpenObservation.Degraded(failures)
        );
        BppLog.DebugEvent(
            CollectionPanelLogEvents.SelectionResolved,
            () =>
                [
                    CollectionPanelLogEvents.SelectionResolvedSource.Bind(
                        selection.SelectedSourceKey
                    ),
                    CollectionPanelLogEvents.SelectionResolvedHero.Bind(selection.SelectedHero),
                    CollectionPanelLogEvents.SelectionResolvedDay.Bind(_currentRunDay),
                    CollectionPanelLogEvents.SelectionResolvedEncounterId.Bind(
                        encounterIds.CurrentEncounterTemplateId
                    ),
                ]
        );
        return selection;
    }

    private bool TryReadIsInGameRunForOpen(List<CollectionPanelSelectionProbeFailure> failures)
    {
        try
        {
            return _services.RunContext.IsInGameRun
                || _services.GameStateProbe.ComputeIsInGameRun();
        }
        catch (Exception ex)
        {
            failures.Add(Failure(CollectionPanelSelectionProbe.RunState, ex));
            return _services.RunContext.IsInGameRun;
        }
    }

    private static EHero? TryReadCurrentHero(List<CollectionPanelSelectionProbeFailure> failures)
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
            failures.Add(Failure(CollectionPanelSelectionProbe.Hero, ex));
            return null;
        }
    }

    private static int? TryReadCurrentDay(List<CollectionPanelSelectionProbeFailure> failures)
    {
        try
        {
            return (int?)TheBazaar.Data.Run?.Day;
        }
        catch (Exception ex)
        {
            failures.Add(Failure(CollectionPanelSelectionProbe.Day, ex));
            return null;
        }
    }

    private EncounterIdsSnapshot TryReadEncounterIds(
        List<CollectionPanelSelectionProbeFailure> failures
    )
    {
        try
        {
            if (_services.EncounterState is ITypedEncounterIdsProbe typedProbe)
            {
                var outcome = typedProbe.GetEncounterIdsOutcome();
                if (outcome.IsSuccess)
                    return outcome.Snapshot;

                failures.Add(
                    Failure(
                        CollectionPanelSelectionProbe.Encounter,
                        outcome.Exception
                            ?? new InvalidOperationException("Encounter ID probe failed.")
                    )
                );
                return outcome.Snapshot;
            }

            return _services.EncounterState.GetEncounterIds();
        }
        catch (Exception ex)
        {
            failures.Add(Failure(CollectionPanelSelectionProbe.Encounter, ex));
            return EncounterIdsSnapshot.Empty;
        }
    }

    private static CollectionPanelSelectionProbeFailure Failure(
        CollectionPanelSelectionProbe probe,
        Exception exception
    ) => new(probe, CollectionPanelLogReasonCode.ProbeReadFailed, exception);

    private void Open(CollectionPanelSelectionState selection)
    {
        ApplyOpenSelection(selection);
        // Temporary main-path probe: EnsureView() is heavy one-time UITK construction (visual
        // tree + CJK glyph raster + cold OTF extract) that runs on the click frame BEFORE the
        // panel is shown and is invisible to CollectionPanelLoadDiagnostics (created later in the
        // coroutine). Time the first construction so its click-frame cost is attributable.
        var firstViewConstruction = _view == null;
        var openPrologueDiagnostics = firstViewConstruction
            ? new CollectionPanelLoadDiagnostics()
            : null;
        EnsureView();
        if (firstViewConstruction)
            openPrologueDiagnostics!.Complete(
                CollectionPanelLoadPhase.OpenPrologue,
                CollectionPanelLoadOutcome.Completed,
                null
            );
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
        // The Day toggle's on/off persists across opens; when it is on, re-pin it to the
        // freshly-read day. _currentRunDay was just captured in ResolveOpenSelection, which always
        // runs before this.
        if (_filter.SelectedRunDay != null)
            _filter.SelectedRunDay = _currentRunDay ?? DayTierSchedule.OutOfRunDay;
        PruneInvisibleSourceSelections();
        _scrollY = 0f;
    }

    private void Close()
    {
        if (!_isVisible)
            return;
        _searchRefreshGate.Cancel();
        // Drop composition state with the panel: a composition whose terminating Count==0
        // event never arrives would otherwise keep Advance() blocked after the next Open().
        DetachImeKeyboard();
        CancelPanelLoad();
        _isVisible = false;
        HideNativeCardLayerImmediately();
        // Let the UITK chrome fade out, but do not keep the native card overlay alive during
        // that animation. CardPreviewBase instances live on a sibling ScreenSpaceOverlay canvas,
        // so delaying its teardown leaves visible card art after the panel has been dismissed.
        _view?.SetVisible(false);
    }

    private void RequestCloseFromUi()
    {
        if (_overlayHandle == null)
        {
            Close();
            return;
        }

        // The visible close button is a lifecycle request, not just a local hide. If this
        // bypasses the host, the dock button's later RequestOpen() sees the panel as still open.
        var outcome = _overlayHandle.RequestClose();
        if (outcome == OverlayRequestOutcome.AlreadyInState && _isVisible)
            Close();
    }

    // Lifecycle (scene change, combat gate, hotkey, escape) is owned by the Overlay Panel Host;
    // this tick carries the panel's own per-frame content work, including closed-state work
    // (fade-out completion, catalog warmup, deferred native cleanup).
    private void Tick(float dt, bool isVisible)
    {
        // Opportunistically warm the card catalog off-thread once static data is ready, so the
        // first panel open does not pay the full-table card read (JsonGameDataManager.GetCardMap
        // -> ReadAllCards) on the main thread. The catalog kicks a single shared Task per
        // static-data source; the first open then awaits it instead of blocking.
        if (!_catalog.HasCardMapLoadStarted)
            _catalog.BeginCardMapLoad(out _);

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

        if (_searchRefreshGate.Advance(dt, isComposing: IsImeCompositionActive()))
        {
            _scrollY = 0f;
            ApplyFilters();
            RefreshView();
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

    private void OnDestroy()
    {
        DetachImeKeyboard();
        if (ReferenceEquals(_instance, this))
            _instance = null;
        _overlayHandle?.Dispose();
        _overlayHandle = null;
        DisposeRuntime();
    }

    private bool IsImeCompositionActive()
    {
        var keyboard = Keyboard.current;
        if (ReferenceEquals(_imeKeyboard, keyboard))
            return _isImeComposing;

        DetachImeKeyboard();
        _imeKeyboard = keyboard;
        if (_imeKeyboard != null)
            _imeKeyboard.onIMECompositionChange += OnImeCompositionChange;
        return false;
    }

    private void OnImeCompositionChange(IMECompositionString composition) =>
        _isImeComposing = composition.Count > 0;

    private void DetachImeKeyboard()
    {
        if (_imeKeyboard != null)
            _imeKeyboard.onIMECompositionChange -= OnImeCompositionChange;
        _imeKeyboard = null;
        _isImeComposing = false;
    }

    private void DisposeRuntime()
    {
        DisposeUnityRuntime();
        InvalidateCatalog(CollectionPanelLogReasonCode.RuntimeDispose);
    }

    private void DisposeUnityRuntime()
    {
        _searchRefreshGate.Cancel();
        CancelPanelLoad();
        var virtualizer = _virtualizer;
        var overlay = _overlay;
        var previewScope = _previewScope;
        var artCache = _artCache;
        var materialCache = _materialCache;
        var cacheSession = _cacheSession;

        virtualizer?.Dispose();
        overlay?.SetAlpha(0f);
        overlay?.SetVisible(false);

        _virtualizer = null;
        _previewScope = null;
        _overlay = null;
        _view?.Dispose();
        _view = null;
        _materialCache = null;
        _artCache = null;
        _cacheSession = null;

        var previewCleanup = previewScope?.DisposeAsync().AsTask() ?? Task.CompletedTask;
        var cleanupBarrier = Task.WhenAll(
            virtualizer?.WhenPendingBindsSettled ?? Task.CompletedTask,
            previewCleanup
        );
        if (cleanupBarrier.IsCompletedSuccessfully)
        {
            DestroyCollectionRuntimeObjects(overlay, cacheSession, artCache, materialCache);
            return;
        }

        _ = DestroyCollectionRuntimeObjectsWhenReadyAsync(
            cleanupBarrier,
            overlay,
            cacheSession,
            artCache,
            materialCache
        );
    }

    private static async Task DestroyCollectionRuntimeObjectsWhenReadyAsync(
        Task cleanupBarrier,
        CollectionGridOverlay? overlay,
        CollectionCardCacheSession? cacheSession,
        CollectionCardArtCache? artCache,
        CollectionCardMaterialCache? materialCache
    )
    {
        try
        {
            await cleanupBarrier;
        }
        catch (Exception ex)
        {
            BppLog.WarnEvent(
                CollectionPanelLogEvents.CleanupDegraded,
                ex,
                CollectionPanelLogEvents.CleanupDegradedReasonCode.Bind(
                    CollectionPanelLogReasonCode.PendingBindWaitFailed
                )
            );
        }

        DestroyCollectionRuntimeObjects(overlay, cacheSession, artCache, materialCache);
    }

    private static void DestroyCollectionRuntimeObjects(
        CollectionGridOverlay? overlay,
        CollectionCardCacheSession? cacheSession,
        CollectionCardArtCache? artCache,
        CollectionCardMaterialCache? materialCache
    )
    {
        // Scope disposal synchronously releases each card's feature-owned tooltip/art/material
        // state before scheduling its GameObject destruction, so cache teardown is now safe.
        overlay?.Dispose();
        CollectionCardCacheHost.Uninstall(cacheSession);
        materialCache?.DisposeAll();
        artCache?.DisposeAll();
    }

    private void EnsureView()
    {
        if (_view != null)
        {
            _view.EnsureCreated();
            return;
        }

        _view = new CollectionPanelView(transform, new PanelCommands(this));

        _view.GridViewportBoundsChanged += bounds =>
        {
            _viewportBoundsPx = bounds;
            _viewportBoundsDirty = true;
        };

        _view.EnsureCreated();

        _overlay = new CollectionGridOverlay();
        _overlay.EnsureInitialized();

        _artCache = new CollectionCardArtCache();
        _materialCache = new CollectionCardMaterialCache();
        _cacheSession = CollectionCardCacheHost.Install(_artCache, _materialCache);

        var previewOwner = new CollectionNativeCardPreviewOwner(_overlay.BoardRoot!, _cacheSession);
        _previewScope = _nativeCardPreviewHost.OpenScope(previewOwner);
        _virtualizer = new CollectionGridVirtualizer(_overlay, _previewScope);
    }

    private static CollectionFacetMatchMode ToggleMatchMode(CollectionFacetMatchMode mode) =>
        mode == CollectionFacetMatchMode.All
            ? CollectionFacetMatchMode.Any
            : CollectionFacetMatchMode.All;

    private sealed class PanelCommands(CollectionPanel panel) : ICollectionPanelCommands
    {
        public void Close() => panel.RequestCloseFromUi();

        public void SetActiveTab(CollectionTabKind tab)
        {
            if (!panel._filter.SelectTab(tab))
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
            panel._filter.ToggleSource(panel._filter.ActiveTab, sourceKey);
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

        public void SetSearchQuery(string query)
        {
            query ??= string.Empty;
            if (string.Equals(panel._filter.SearchQuery, query, StringComparison.Ordinal))
                return;

            panel._filter.SearchQuery = query;
            panel._searchRefreshGate.Schedule();
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
        CollectionPanelLogReasonCode? unavailableReason = null;
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
            diagnostics.AddSegment(CollectionPanelLoadSegment.CatalogAcquire, acquireStarted);
            var mapOutcome = CollectionCardMapLoadOutcome.From(source, loadTask);

            if (_catalog.TryCreateBuildSession(mapOutcome, out var session, out unavailableReason))
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
            }
        }
        diagnostics.AddSegment(CollectionPanelLoadSegment.Catalog, started);
        if (catalogResult != null)
        {
            diagnostics.SetCatalogResult(
                catalogResult.WasCacheHit,
                catalogResult.SourceTemplateCount,
                catalogResult.AcceptedCount,
                catalogResult.RejectedCount
            );
        }

        if (!IsLoadGenerationCurrent(generation))
            yield break;

        started = diagnostics.Now();
        ApplyFilters();
        diagnostics.AddSegment(CollectionPanelLoadSegment.Filter, started);

        started = diagnostics.Now();
        _isLoadingCatalog = false;
        if (_catalogCards.Count > 0)
            ClearStatus();
        RefreshView();
        diagnostics.AddSegment(CollectionPanelLoadSegment.Refresh, started);
        diagnostics.SetFinalCounts(_catalogCards.Count, _virtualizer?.VisibleCount ?? 0);
        diagnostics.Complete(
            CollectionPanelLoadPhase.PanelLoad,
            _catalogCards.Count > 0
                ? CollectionPanelLoadOutcome.Loaded
                : CollectionPanelLoadOutcome.Unavailable,
            unavailableReason
        );
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

        _virtualizer.SetVisible(Array.Empty<CollectionCardVm>(), _filter.ActiveTab);
        _view?.ResetScroll();
        _scrollY = 0f;
    }

    private void ApplyFilters()
    {
        _searchRefreshGate.Cancel();
        if (_virtualizer == null)
            return;

        if (_catalogCards.Count == 0)
        {
            _virtualizer.SetVisible(Array.Empty<CollectionCardVm>(), _filter.ActiveTab);
        }
        else
        {
            if (!_isLoadingCatalog)
                ClearStatus();
            var query = CollectionQuery.Run(
                _catalogCards,
                _filter,
                _facetAvailability,
                _sourceCatalog,
                _offerPoolCache
            );
            AdoptNormalization(query.Normalization);
            _virtualizer.SetVisible(query.Cards, _filter.ActiveTab, query.OfferMatchesByCardId);
        }
        ResetVisibleScroll();
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
    }

    private void RefreshView()
    {
        if (_view == null || _virtualizer == null)
            return;

        var profile = CollectionTabProfile.For(_filter.ActiveTab);
        var availableTags = _facetAvailability.ItemTags;
        var availableKeywords = _facetAvailability.KeywordsFor(_filter.ActiveType);
        var dayFilterPresentation = CollectionDayFilterPresentation.For(
            profile,
            _filter.SelectedRunDay != null
        );
        var heroFilterPresentation = CollectionHeroFilterPresentation.For(profile);
        var model = new CollectionPanelViewModel
        {
            Title = CollectionPanelText.Title(),
            Subtitle = CollectionPanelText.Subtitle(),
            Supporters = _supporters,
            CountText = CollectionPanelText.MatchCount(_virtualizer.VisibleCount),
            StatusMessage = _statusVisible ? _statusMessage : null,
            IsLoading = _isLoadingCatalog,
            ActiveTab = _filter.ActiveTab,
            ActiveType = _filter.ActiveType,
            TabProfile = profile,
            HeroFilterVisible = heroFilterPresentation.IsVisible,
            HeroFilterEnabled = heroFilterPresentation.IsEnabled,
            // The view only does Contains lookups on these inside the synchronous Refresh and
            // never retains the model, so the live filter sets are shared instead of copied.
            SelectedHeroes = _filter.Heroes,
            SelectedTiers = _filter.Tiers,
            SelectedSizes = _filter.Sizes,
            SelectedTags = _filter.Tags,
            SelectedKeywords = _filter.Keywords,
            TagMatchMode = _filter.TagMatchMode,
            KeywordMatchMode = _filter.KeywordMatchMode,
            SearchQuery = _filter.SearchQuery,
            SelectedSourceKey = profile.ShowSourceFilter ? _filter.SelectedSourceKey : null,
            SourceSelectorEnabled = profile.ShowSourceFilter && !_isLoadingCatalog,
            SortPriority = _filter.SortPriority,
            DayFilterVisible = dayFilterPresentation.IsVisible,
            DayFilterEnabled = dayFilterPresentation.IsEnabled,
            DayFilterActive = dayFilterPresentation.IsActive,
            DayFilterValue = _currentRunDay ?? DayTierSchedule.OutOfRunDay,
            AvailableHeroes = HeroOrder,
            AvailableTiers = TierOrder,
            AvailableSizes = SizeOrder,
            AvailableTags = availableTags,
            AvailableKeywords = availableKeywords,
            AvailableSources = AvailableSourcesFor(_filter.ActiveTab),
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

    private IReadOnlyList<CollectionSourceOptionViewModel> AvailableSourcesFor(
        CollectionTabKind activeTab
    )
    {
        var sourceKind = CollectionTabProfile.For(activeTab).SourceKind;
        if (!sourceKind.HasValue)
            return Array.Empty<CollectionSourceOptionViewModel>();

        var kind = sourceKind.Value;
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
        var sourceKind = CollectionTabProfile.For(_filter.ActiveTab).SourceKind;
        if (!sourceKind.HasValue)
            return _filter.ClearSelectedSource();

        var visibleSources = SourceKeysFor(sourceKind.Value, selectedHero);
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

    private void InvalidateCatalog(CollectionPanelLogReasonCode reasonCode)
    {
        SetCatalogCards(Array.Empty<CollectionCardVm>());
        _offerPoolCache.Clear();
        _catalog.InvalidateCache(reasonCode);
    }

    // Facet availability is a pure projection of the immutable catalog, so it is recomputed
    // only here — at the points where the catalog itself changes — never per RefreshView.
    private void SetCatalogCards(IReadOnlyList<CollectionCardVm> cards)
    {
        _catalogCards = cards;
        _facetAvailability = CollectionFacetAvailability.SnapshotFor(cards);
    }
}
