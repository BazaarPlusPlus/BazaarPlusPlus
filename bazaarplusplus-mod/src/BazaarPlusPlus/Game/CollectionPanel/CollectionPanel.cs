#nullable enable
using System.Collections;
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Core.Cards;
using BazaarPlusPlus.Core.Config;
using BazaarPlusPlus.Core.GameState;
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.Game.CollectionPanel.Data;
using BazaarPlusPlus.Game.CollectionPanel.Grid;
using BazaarPlusPlus.Game.CollectionPanel.Sources;
using BazaarPlusPlus.Game.CollectionPanel.Ui;
using BazaarPlusPlus.Game.Input;
using BazaarPlusPlus.Game.OverlayPanels;
using BazaarPlusPlus.Game.Supporters;
using BazaarPlusPlus.GameInterop.CardPreview;
using BazaarPlusPlus.GameInterop.Cards;
using BazaarPlusPlus.GameInterop.DayTiers;
using BazaarPlusPlus.GameInterop.StaticCards;
using BazaarPlusPlus.GameInterop.TagTypography;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace BazaarPlusPlus.Game.CollectionPanel;

internal sealed class CollectionPanel : MonoBehaviour
{
    private const float CatalogBuildFrameBudgetMs = 4f;
    private const float CatalogWarmupFrameBudgetMs = 1f;
    private const string OverlayPanelId = "CollectionPanel";

    private const float ClosedRuntimeReleaseDelaySeconds = 60f;

    private static CollectionPanel? _instance;
    public static bool IsVisible => _instance != null && _instance._isVisible;
    internal static CollectionPanel? Instance => _instance;

    private CollectionCatalog _catalog = null!;
    private Keyboard? _imeKeyboard;
    private bool _isImeComposing;
    private readonly CollectionPanelHeroPreferenceStore _heroPreferenceStore = new();
    private readonly CollectionPanelSelectionLogState _selectionLogState = new();
    private readonly CollectionGridPortAdapter _gridPort = new();
    private CollectionViewState _viewState = null!;

    private BppSettings _config = null!;
    private INativeCardPreviewHost _nativeCardPreviewHost = null!;
    private CollectionInspectionModule _inspection = null!;
    private readonly CollectionInspectionClick _inspectionClick = new();
    private GameDataDayTierResolver _dayTierResolver = null!;
    private CollectionPanelView? _view;
    private CollectionGridOverlay? _overlay;
    private INativeCardPreviewScope? _previewScope;
    private CollectionGridVirtualizer? _virtualizer;
    private CollectionCardArtCache? _artCache;
    private CollectionCardMaterialCache? _materialCache;
    private CollectionCardCacheSession? _cacheSession;

    private IBppServices _services = null!;
    private IOverlayPanelHandle? _overlayHandle;
    private bool _isVisible;
    private bool _initialized;
    private bool _viewportBoundsDirty;
    private Rect _viewportBoundsPx;
    private float _scrollY;
    private Coroutine? _loadCoroutine;
    private int _loadGeneration;
    private float _closedIdleSeconds;

    private bool _viewMissedNativeTypography;
    private bool _sourceCatalogWarmed;
    private bool _stagingItemIdCopyEnabled;

    public void Initialize(
        IBppServices services,
        BppStaticCardMapProvider cardMapProvider,
        INativeCardPreviewHost nativeCardPreviewHost,
        GameDataDayTierResolver dayTierResolver,
        CollectionInspectionModule inspection
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
        if (dayTierResolver == null)
            throw new ArgumentNullException(nameof(dayTierResolver));

        _initialized = true;
        _instance = this;
        _services = services;
        _config = services.Config;
        _stagingItemIdCopyEnabled = CollectionStagingTools.IsEnabled(services.GameBuild.RawVersion);
        _nativeCardPreviewHost = nativeCardPreviewHost;
        _inspection = inspection;
        _inspection.Attach(
            nativeCardPreviewHost,
            SetInspectionBlocked,
            () => _virtualizer?.HoveredScreenRect() ?? default
        );
        _dayTierResolver = dayTierResolver;
        _catalog = new CollectionCatalog(cardMapProvider);
        _viewState = new CollectionViewState(
            _gridPort,
            StaticCollectionSourceCatalog.Instance,
            new CollectionDayTierSource(dayTierResolver),
            _heroPreferenceStore,
            foldSources: true
        );
    }

    internal void AttachToOverlayHost(DesktopOverlayPanels overlayHost)
    {
        if (_overlayHandle != null)
            return;

        _overlayHandle = overlayHost.Register(
            new OverlayPanelRegistration(
                OverlayPanelId,
                onOpen: () =>
                {
                    Open();
                    return true;
                },
                onClose: _ => Close(),
                tick: Tick
            )
            {
                OnSceneChanged = DisposeUnityRuntime,
                IsModalInputActive = () =>
                    _inspection.BlocksInput || _view?.IsSourceFoldOpen == true,
                DismissModal = () =>
                {
                    if (_inspection.BlocksInput)
                        _inspection.Close();
                    else
                        _view?.CollapseSourceFold();
                },
            },
            BppHotkeyActionId.ToggleCollectionPanel,
            hotkeyGuard: () => !IsVisible || !IsTextInputFocused()
        );
    }

    private bool IsTextInputFocused() => _view?.IsTextInputFocused() == true;

    private void SetInspectionBlocked(bool blocked)
    {
        _view?.SetInspectionBlocked(blocked);
        _virtualizer?.SetInspectionBlocked(blocked);
        if (blocked)
            DetachImeKeyboard();
    }

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
                new BppLogEvent(BppLogFeatureScope.CollectionPanel, "collection_panel.open.failed"),
                ("reason_code", CollectionPanelLogReasonCode.NotMounted)
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
                    new BppLogEvent(
                        BppLogFeatureScope.CollectionPanel,
                        "collection_panel.open.skipped"
                    ),
                    static () => [("reason_code", CollectionPanelLogReasonCode.CombatActive)]
                );
                return;
            case OverlayRequestOutcome.Disabled:
                BppLog.DebugEvent(
                    new BppLogEvent(
                        BppLogFeatureScope.CollectionPanel,
                        "collection_panel.open.skipped"
                    ),
                    static () => [("reason_code", CollectionPanelLogReasonCode.PanelDisabled)]
                );
                return;
            case OverlayRequestOutcome.OpenFailed:
                LogOpenFailed(CollectionPanelLogReasonCode.OpenFailed);
                return;
            case OverlayRequestOutcome.Busy:
                LogOpenFailed(CollectionPanelLogReasonCode.OverlayHostBusy);
                return;
            case OverlayRequestOutcome.UnknownPanel:
            default:
                LogOpenFailed(CollectionPanelLogReasonCode.UnknownPanel);
                return;
        }

        static void LogOpenFailed(CollectionPanelLogReasonCode reasonCode) =>
            BppLog.ErrorEvent(
                new BppLogEvent(BppLogFeatureScope.CollectionPanel, "collection_panel.open.failed"),
                ("reason_code", reasonCode)
            );
    }

    private void Open()
    {
        var failures = new List<CollectionPanelSelectionProbeFailure>(5);
        var cachedCatalog = _catalog.TryGetCached(out var cached) ? cached.Cards : null;
        var supporters = BPPSupporters.SampleMany(4);
        CollectionOpenFacts facts;
        if (TryReadIsInGameRunForOpen(failures))
        {
            var hero = TryReadCurrentHero(failures);
            var encounter = TryReadEncounterIds(failures);
            var day = TryReadCurrentDay(failures);
            facts = CollectionOpenFacts.InRun(hero, day, encounter, cachedCatalog, supporters);
        }
        else
            facts = CollectionOpenFacts.OutOfRun(cachedCatalog, supporters);
        var opened = _viewState.Open(facts);
        if (opened.HeroPreferenceFailure is { } heroPreferenceFailure)
            failures.Add(
                Failure(CollectionPanelSelectionProbe.HeroPreference, heroPreferenceFailure)
            );

        _selectionLogState.ObserveOpen(
            failures.Count == 0
                ? CollectionPanelSelectionOpenObservation.Complete()
                : CollectionPanelSelectionOpenObservation.Degraded(failures)
        );
        BppLog.DebugEvent(
            new BppLogEvent(
                BppLogFeatureScope.CollectionPanel,
                "collection_panel.selection.resolved"
            ),
            () =>
                [
                    ("source", opened.Selection.SelectedSourceKey),
                    ("hero", opened.Selection.SelectedHero),
                    ("day", facts.RunDay),
                    ("encounter_id", facts.Encounter.CurrentEncounterTemplateId),
                ]
        );

        _closedIdleSeconds = 0f;
        EnsureView();
        // After EnsureView: ApplyOutcome drops the render while the view does not exist.
        ApplyOutcome(opened.Render);
        _isVisible = true;
        _view!.SetVisible(true);
        _overlay?.SetVisible(true);
        _overlay?.SetAlpha(_view!.CurrentOpacity);
        StartPanelLoad();
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

    private static CardHero? ToFacet(EHero? hero) =>
        hero.HasValue && DesktopCardFacets.Hero.TryToFacet(hero.Value, out var facet)
            ? facet
            : null;

    private static CardHero? TryReadCurrentHero(List<CollectionPanelSelectionProbeFailure> failures)
    {
        try
        {
            var runHero = ToFacet(TheBazaar.Data.Run?.Player?.Hero);
            if (CollectionPanelSelectionState.IsConcreteHero(runHero))
                return runHero;

            var selectedHero = ToFacet(TheBazaar.Data.SelectedHero);
            return CollectionPanelSelectionState.IsConcreteHero(selectedHero) ? selectedHero : null;
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
            var outcome = _services.EncounterState.GetEncounterIdsOutcome();
            if (outcome.IsSuccess)
                return outcome.Snapshot;

            failures.Add(
                Failure(
                    CollectionPanelSelectionProbe.Encounter,
                    outcome.Exception ?? new InvalidOperationException("Encounter ID probe failed.")
                )
            );
            return outcome.Snapshot;
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

    private void Close()
    {
        if (!_isVisible)
            return;
        _inspection.Close();
        ApplyOutcome(_viewState.Close());
        _heroPreferenceStore.Flush();
        // Drop composition state with the panel: a composition whose terminating Count==0
        // event never arrives would otherwise keep Advance() blocked after the next Open().
        DetachImeKeyboard();
        CancelPanelLoad();
        _isVisible = false;
        HideNativeCardLayerImmediately();
        // Native cards live on a sibling canvas and would outlast the dismissal if hidden after the UITK fade.
        _view?.SetVisible(false);
    }

    private void RequestCloseFromUi()
    {
        if (_overlayHandle == null)
        {
            Close();
            return;
        }

        // Bypassing the host leaves the dock button's next RequestOpen() treating the panel as open.
        var outcome = _overlayHandle.RequestClose();
        if (outcome == OverlayRequestOutcome.AlreadyInState && _isVisible)
            Close();
    }

    private void Tick(float dt, bool isVisible)
    {
        _inspection.Tick(dt);
        // Closed-state warmup and the open panel share one build session to avoid a second full-table walk.
        _catalog.AdvanceBuild(
            isVisible ? CatalogBuildFrameBudgetMs : CatalogWarmupFrameBudgetMs,
            out _
        );
        if (!_sourceCatalogWarmed)
        {
            _ = StaticCollectionSourceCatalog.Entries;
            _sourceCatalogWarmed = true;
        }

        // A close still needs fade ticks to finish before runtime teardown.
        _view?.TickOpacity(dt);
        _view?.TickLoading(dt);
        _view?.TickControlsScrollShadow();
        if (_view != null && _overlay != null)
            _overlay.SetAlpha(_view.CurrentOpacity);

        if (!_isVisible)
        {
            _inspectionClick.Reset();
            if (_view != null && !_view.IsFadingOrVisible)
            {
                _overlay?.SetVisible(false);
                _virtualizer?.Dispose();
                _closedIdleSeconds += dt;
                if (_closedIdleSeconds >= ClosedRuntimeReleaseDelaySeconds)
                    ReleaseClosedRuntime();
            }
            return;
        }

        if (_inspection.BlocksInput)
        {
            _inspectionClick.Reset();
            return;
        }

        ApplyOutcome(_viewState.TickSearch(dt, isComposing: IsImeCompositionActive()));

        // Game tooltip typography registers asynchronously, so degraded startup chips need a refresh when it appears.
        if (_viewMissedNativeTypography && NativeTagTypography.IsNativeTypographyAvailable)
            ApplyOutcome(_viewState.Rebuild());

        if (_viewportBoundsDirty && _virtualizer != null && _overlay != null)
        {
            _viewportBoundsDirty = false;
            _overlay.SetPosition(_viewportBoundsPx.position);
            _overlay.SetClipSize(_viewportBoundsPx.size);
            _virtualizer.SetViewport(_viewportBoundsPx.width, _viewportBoundsPx.height);
            // The placeholder width can leave the bottom rows unreachable until the scroll spacer uses the measured viewport.
            _view?.UpdateContentSpacerHeight(_virtualizer.ContentHeight);
        }

        // UI Toolkit's default wheel handler writes scrollOffset directly.
        if (_view != null)
            _scrollY = _view.ReadScrollYPixels();
        _virtualizer?.SetScrollY(_scrollY);
        _virtualizer?.Tick();
        _virtualizer?.TickFades(dt);

        if (CollectionGridHostConstants.UsePolledHover && _virtualizer != null)
        {
            var mouse = Mouse.current;
            if (mouse != null)
            {
                var pos = mouse.position.ReadValue();
                _virtualizer.PollHover(pos, _viewportBoundsPx);
                TryCopyHoveredCardId();
                TryOpenHoveredInspection(mouse, pos);
            }
            else
                _inspectionClick.Reset();
        }
        _virtualizer?.TickHoverScale(dt);
    }

    private void TryOpenHoveredInspection(Mouse mouse, Vector2 position)
    {
        if (!Application.isFocused)
        {
            _inspectionClick.Reset();
            return;
        }
        Guid? id =
            _virtualizer != null && _virtualizer.TryGetHoveredCard(out var card) ? card.Id : null;
        if (
            _inspectionClick.Sample(
                mouse.leftButton.wasPressedThisFrame,
                mouse.leftButton.wasReleasedThisFrame,
                id,
                position.x,
                position.y,
                _scrollY
            ) && id.HasValue
        )
            _inspection.OpenHovered(id.Value);
    }

    private void TryCopyHoveredCardId()
    {
        if (
            !_stagingItemIdCopyEnabled
            || IsTextInputFocused()
            || _virtualizer == null
            || !_virtualizer.TryGetHoveredCard(out var card)
        )
            return;

        var keyboard = Keyboard.current;
        if (
            keyboard?.cKey.wasPressedThisFrame != true
            || (keyboard.leftCtrlKey.isPressed || keyboard.rightCtrlKey.isPressed) != true
        )
            return;

        var templateId = card.Id.ToString("D");
        GUIUtility.systemCopyBuffer = templateId;
        _view?.ShowStagingTemplateIdCopied(card.DisplayName, templateId);
    }

    private void ReleaseClosedRuntime()
    {
        _closedIdleSeconds = 0f;
        DisposeUnityRuntime();
        BppLog.InfoEvent(
            new BppLogEvent(
                BppLogFeatureScope.CollectionPanel,
                "collection_panel.runtime.released"
            ),
            ("idle_seconds", ClosedRuntimeReleaseDelaySeconds)
        );
    }

    private void HideNativeCardLayerImmediately()
    {
        _virtualizer?.Dispose();
        _overlay?.SetAlpha(0f);
        _overlay?.SetVisible(false);
    }

    // On desktop, Unity sends OnApplicationPause only with Run In Background off.
    private void OnApplicationFocus(bool focused)
    {
        if (!focused)
            _heroPreferenceStore.Flush();
    }

    private void OnApplicationPause(bool paused)
    {
        if (paused)
            _heroPreferenceStore.Flush();
    }

    private void OnDestroy()
    {
        _heroPreferenceStore.Flush();
        _inspection?.Detach();
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
        _inspection?.Close();
        _viewState.Close();
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
        _gridPort.Bind(null);
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
                new BppLogEvent(
                    BppLogFeatureScope.CollectionPanel,
                    "collection_panel.cleanup.degraded",
                    storm: ["reason_code"]
                ),
                ex,
                ("reason_code", CollectionPanelLogReasonCode.PendingBindWaitFailed)
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
        // Scope disposal releases card references synchronously, before Unity destroys their GameObjects.
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

        _view = new CollectionPanelView(
            transform,
            new PanelCommands(this),
            _stagingItemIdCopyEnabled
        );

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
        _gridPort.Bind(_virtualizer);
    }

    private sealed class PanelCommands(CollectionPanel panel) : ICollectionPanelCommands
    {
        public void Close() => panel.RequestCloseFromUi();

        public void ToggleSearch() => panel.ApplyOutcome(panel._viewState.ToggleSearch());

        public void SetActiveTab(CollectionTabKind tab) =>
            panel.ApplyOutcome(panel._viewState.SetActiveTab(tab));

        public void ResetFilters() => panel.ApplyOutcome(panel._viewState.ResetFilters());

        public void ToggleHero(CardHero hero) =>
            panel.ApplyOutcome(panel._viewState.ToggleHero(hero));

        public void ToggleAllHeroes() => panel.ApplyOutcome(panel._viewState.ToggleAllHeroes());

        public void ToggleTier(CardTier tier) =>
            panel.ApplyOutcome(panel._viewState.ToggleTier(tier));

        public void ToggleRunDayFilter() =>
            panel.ApplyOutcome(panel._viewState.ToggleRunDayFilter());

        public void ToggleSize(CardSize size) =>
            panel.ApplyOutcome(panel._viewState.ToggleSize(size));

        public void ToggleTag(CardTag tag) => panel.ApplyOutcome(panel._viewState.ToggleTag(tag));

        public void ToggleKeyword(CollectionKeywordFacetOption option) =>
            panel.ApplyOutcome(panel._viewState.ToggleKeyword(option));

        public void SetKeywordMatchMode(CollectionFacetMatchMode mode) =>
            panel.ApplyOutcome(panel._viewState.SetKeywordMatchMode(mode));

        public void SetTagMatchMode(CollectionFacetMatchMode mode) =>
            panel.ApplyOutcome(panel._viewState.SetTagMatchMode(mode));

        public void ToggleSource(string sourceKey) =>
            panel.ApplyOutcome(panel._viewState.ToggleSource(sourceKey));

        public void SetSortPriority(CollectionSortPriority priority) =>
            panel.ApplyOutcome(panel._viewState.SetSortPriority(priority));

        public void SetSearchQuery(string query) => panel._viewState.SetSearchQuery(query);
    }

    private void ApplyOutcome(CollectionRenderOutcome? outcome)
    {
        if (outcome == null)
            return;

        if (outcome.ResetScroll)
        {
            _view?.ResetScroll();
            _scrollY = 0f;
        }

        if (outcome.ResetControlsScroll)
            _view?.ResetControlsScroll();

        // Typography cannot register during this synchronous refresh. Set the flag first so a failed refresh cannot arm a per-frame retry.
        _viewMissedNativeTypography = !NativeTagTypography.IsNativeTypographyAvailable;
        _view?.Refresh(outcome.Model);
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
    }

    private IEnumerator LoadPanelAsync(int generation)
    {
        var diagnostics = new CollectionPanelLoadDiagnostics();
        ApplyOutcome(_viewState.BeginCatalogLoad());

        yield return null;

        if (!IsLoadGenerationCurrent(generation))
            yield break;

        var started = diagnostics.Now();
        CollectionCatalogBuildResult? catalogResult = null;
        CollectionPanelLogReasonCode? unavailableReason = null;
        CollectionRenderOutcome? acceptOutcome = null;
        while (true)
        {
            if (_catalog.TryGetCached(out var cached))
            {
                catalogResult = cached;
                acceptOutcome = _viewState.AcceptCatalog(cached.Cards);
                break;
            }

            if (_catalog.WarmupStatus == CollectionCatalogWarmupStatus.Unavailable)
            {
                unavailableReason = _catalog.WarmupFailureReason;
                acceptOutcome = _viewState.CatalogUnavailable();
                break;
            }

            if (!IsLoadGenerationCurrent(generation))
                yield break;
            yield return null;
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
        ApplyOutcome(acceptOutcome);
        diagnostics.AddSegment(CollectionPanelLoadSegment.Filter, started);
        diagnostics.AddSegment(CollectionPanelLoadSegment.Refresh, started);
        diagnostics.SetFinalCounts(_viewState.CatalogCardCount, _gridPort.Current.VisibleCount);
        diagnostics.Complete(
            CollectionPanelLoadPhase.PanelLoad,
            _viewState.CatalogCardCount > 0
                ? CollectionPanelLoadOutcome.Loaded
                : CollectionPanelLoadOutcome.Unavailable,
            unavailableReason
        );
        _loadCoroutine = null;
    }

    private bool IsLoadGenerationCurrent(int generation) =>
        generation == _loadGeneration && _isVisible;

    private void InvalidateCatalog(CollectionPanelLogReasonCode reasonCode)
    {
        _viewState.ResetCatalog();
        _catalog.InvalidateCache(reasonCode);
    }
}
