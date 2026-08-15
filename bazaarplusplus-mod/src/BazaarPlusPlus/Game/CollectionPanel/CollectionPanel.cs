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
using BazaarPlusPlus.Game.Input;
using BazaarPlusPlus.Game.OverlayPanels;
using BazaarPlusPlus.Game.Supporters;
using BazaarPlusPlus.GameInterop.CardPreview;
using BazaarPlusPlus.GameInterop.DayTiers;
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
    private const float CatalogWarmupFrameBudgetMs = 1f;
    private const string OverlayPanelId = "CollectionPanel";

    private static CollectionPanel? _instance;
    public static bool IsVisible => _instance != null && _instance._isVisible;
    internal static CollectionPanel? Instance => _instance;

    private CollectionCatalog _catalog = null!;
    private Keyboard? _imeKeyboard;
    private bool _isImeComposing;
    private readonly ICollectionPanelHeroPreferenceStore _heroPreferenceStore =
        new CollectionPanelHeroPreferenceStore();
    private readonly CollectionPanelSelectionLogState _selectionLogState = new();
    private readonly CollectionGridPortAdapter _gridPort = new();
    private CollectionViewState _viewState = null!;

    private IBppConfig _config = null!;
    private INativeCardPreviewHost _nativeCardPreviewHost = null!;
    private IGameDataDayTierResolver _dayTierResolver = null!;
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

    // True when the last render ran before the game's async tooltip typography registration
    // completed: tag chips rendered degraded (string-table labels, no accent color) and nothing
    // else would re-render them without user interaction. Update polls for the typography
    // instance and re-renders once, so the startup window self-heals.
    private bool _viewMissedNativeTypography;
    private bool _sourceCatalogWarmed;
    private bool _stagingItemIdCopyEnabled;

    public void Initialize(
        IBppServices services,
        BppStaticCardMapProvider cardMapProvider,
        INativeCardPreviewHost nativeCardPreviewHost,
        IGameDataDayTierResolver dayTierResolver
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
        _dayTierResolver = dayTierResolver;
        _catalog = new CollectionCatalog(cardMapProvider);
        _viewState = new CollectionViewState(
            _gridPort,
            new StaticCollectionSourceCatalog(),
            dayTierResolver,
            _heroPreferenceStore
        );
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

    private void Open()
    {
        var resolved = ResolveOpenSelection();
        Open(resolved.Selection, resolved.CurrentRunDay, resolved.EncounteredMerchantSourceKeys);
    }

    private (
        CollectionPanelSelectionState Selection,
        int? CurrentRunDay,
        IReadOnlyList<string> EncounteredMerchantSourceKeys
    ) ResolveOpenSelection()
    {
        PrepareCatalogReadinessForOpen();
        var failures = new List<CollectionPanelSelectionProbeFailure>(4);
        var isInGameRun = TryReadIsInGameRunForOpen(failures);
        var rememberedPreference = isInGameRun
            ? null
            : _heroPreferenceStore.Load(_viewState.CatalogReadiness, _viewState.AvailableHeroes);
        var hero = isInGameRun ? TryReadCurrentHero(failures) : null;
        var encounterIds = isInGameRun ? TryReadEncounterIds(failures) : EncounterIdsSnapshot.Empty;
        var currentRunDay = isInGameRun ? TryReadCurrentDay(failures) : null;
        var sourceEntries = CollectionSourceCatalog.Entries;
        var selection = CollectionPanelOpenSelectionResolver.Resolve(
            isInGameRun,
            hero,
            encounterIds.CurrentEncounterTemplateId,
            encounterIds.ChoiceSelectionTemplateIds,
            sourceEntries,
            rememberedPreference
        );
        var encounteredMerchantSourceKeys = isInGameRun
            ? CollectionPanelOpenSelectionResolver.ResolveEncounteredMerchantSourceKeys(
                hero,
                encounterIds.ChoiceSelectionTemplateIds,
                sourceEntries
            )
            : Array.Empty<string>();

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
                    CollectionPanelLogEvents.SelectionResolvedDay.Bind(currentRunDay),
                    CollectionPanelLogEvents.SelectionResolvedEncounterId.Bind(
                        encounterIds.CurrentEncounterTemplateId
                    ),
                ]
        );
        return (selection, currentRunDay, encounteredMerchantSourceKeys);
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

    private void Open(
        CollectionPanelSelectionState selection,
        int? currentRunDay,
        IReadOnlyCollection<string> encounteredMerchantSourceKeys
    )
    {
        _viewState.ResetSearchForLifecycle();
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
        ApplyOutcome(
            _viewState.ApplyOpenSelection(
                selection,
                BPPSupporters.SampleMany(4),
                currentRunDay,
                encounteredMerchantSourceKeys
            )
        );
        _isVisible = true;
        // SetVisible starts the fade-in ramp; overlay activates so its CanvasGroup starts
        // mirroring the view's opacity (Update pushes the live value each frame).
        _view!.SetVisible(true);
        _overlay?.SetVisible(true);
        _overlay?.SetAlpha(_view!.CurrentOpacity);
        StartPanelLoad();
    }

    private void PrepareCatalogReadinessForOpen()
    {
        if (_catalog.TryGetCached(out var cached))
        {
            _viewState.PrepareCatalogForOpen(cached.Cards);
            return;
        }

        _viewState.PrepareCatalogForOpen(cachedCards: null);
    }

    private void Close()
    {
        if (!_isVisible)
            return;
        _viewState.ResetSearchForLifecycle();
        // Cancel a deferred focus requested by the expanded presentation before the fading view
        // remains alive for another frame.
        ApplyOutcome(_viewState.Rebuild());
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
        // Warm the shared card map and catalog while the panel is closed. Card-map acquisition
        // runs on the provider's worker task; catalog projection stays on the main thread but is
        // capped to a small closed-state budget. An open panel raises the budget and consumes the
        // same build session, so the first click never starts a duplicate full-table walk.
        _catalog.AdvanceBuild(
            isVisible ? CatalogBuildFrameBudgetMs : CatalogWarmupFrameBudgetMs,
            out _
        );
        if (!_sourceCatalogWarmed)
        {
            _ = CollectionSourceCatalog.Entries;
            _sourceCatalogWarmed = true;
        }

        // Drive the panel fade every frame regardless of _isVisible so a Close mid-frame
        // can finish its fade-out animation before we tear runtime down.
        _view?.TickOpacity(dt);
        _view?.TickLoading(dt);
        _view?.TickControlsScrollShadow();
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

        ApplyOutcome(_viewState.TickSearch(dt, isComposing: IsImeCompositionActive()));

        // Startup self-heal for tag typography: a refresh that ran inside the game's async
        // typography registration window rendered degraded chips, and no further Refresh
        // arrives without user interaction. Re-render once the instance appears (the check is
        // a static null probe per frame; ApplyOutcome clears the flag).
        if (_viewMissedNativeTypography && NativeTagTypography.IsNativeTypographyAvailable)
            ApplyOutcome(_viewState.Rebuild());

        if (_viewportBoundsDirty && _virtualizer != null && _overlay != null)
        {
            _viewportBoundsDirty = false;
            _overlay.SetPosition(_viewportBoundsPx.position);
            _overlay.SetClipSize(_viewportBoundsPx.size);
            _virtualizer.SetViewport(_viewportBoundsPx.width, _viewportBoundsPx.height);
            // The base unit (and therefore ContentHeight) is derived from the viewport width,
            // so re-publish the scroll-spacer height once real bounds arrive — otherwise the
            // first-open estimate computed at the placeholder width leaves the bottom rows
            // unreachable. Viewport-driven projection drift stays panel-owned (grid port contract).
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
                TryCopyHoveredCardId();
            }
        }
        _virtualizer?.TickHoverScale(dt);
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
        _viewState.ResetSearchForLifecycle();
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

        public void ToggleHero(EHero hero) => panel.ApplyOutcome(panel._viewState.ToggleHero(hero));

        public void ToggleAllHeroes() => panel.ApplyOutcome(panel._viewState.ToggleAllHeroes());

        public void ToggleTier(ETier tier) => panel.ApplyOutcome(panel._viewState.ToggleTier(tier));

        public void ToggleRunDayFilter() =>
            panel.ApplyOutcome(panel._viewState.ToggleRunDayFilter());

        public void ToggleSize(ECardSize size) =>
            panel.ApplyOutcome(panel._viewState.ToggleSize(size));

        public void ToggleTag(ECardTag tag) => panel.ApplyOutcome(panel._viewState.ToggleTag(tag));

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

        // Record whether this render has native typography; while it does not, Update polls for
        // the late async registration and re-renders so the degraded chips self-heal. Written
        // BEFORE the render: typography registration is a main-thread continuation that cannot
        // interleave with the synchronous Refresh below, so the value is identical either way,
        // and writing first keeps a throwing Refresh from leaving the flag armed (which would
        // turn a one-shot failure into a per-frame retry).
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
        _viewState?.NoteCatalogLoadCancelled();
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

        // AcceptCatalog/CatalogUnavailable fold the prior Filter + Refresh segments into one
        // query-and-render (allowed measurement micro-shift; event schema unchanged).
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
