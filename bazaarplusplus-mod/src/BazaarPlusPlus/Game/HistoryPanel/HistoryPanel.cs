#nullable enable
using BazaarPlusPlus.Game.HistoryPanel.Data;
using BazaarPlusPlus.Game.HistoryPanel.Ghost;
using BazaarPlusPlus.Game.HistoryPanel.Storage;
using BazaarPlusPlus.Game.Input;
using BazaarPlusPlus.Game.OverlayPanels;
using BazaarPlusPlus.Game.Supporters;
using BazaarPlusPlus.Game.Tooltips;
using BazaarPlusPlus.GameInterop.CardPreview;
using BazaarPlusPlus.GameInterop.ItemBoardPreview;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.UiTokens;
using UnityEngine;
using UnityEngine.InputSystem;
using Coroutine = UnityEngine.Coroutine;

namespace BazaarPlusPlus.Game.HistoryPanel;

internal sealed partial class HistoryPanel : MonoBehaviour
{
    private const string OverlayPanelId = "HistoryPanel";

    internal static HistoryPanel? Instance { get; private set; }

    private readonly HistoryPanelState _state = new();
    private readonly HistoryPanelPayloadFailureLogGate _payloadFailureLogGate = new();
    private HistoryPanelDependencies? _dependencies;
    private HistoryPanelCoordinator? _coordinator;
    private HistoryPanelDataService _dataService = null!;
    private HistoryPanelReplayService _replayService = null!;
    private INativeCardPreviewHost _nativeCardPreviewHost = null!;
    private BppItemBoardPreview? _battleBoardPreview;
    private IHistoryPanelRuntime? _runtime;
    private Coroutine? _previewCoroutine;
    private IReadOnlyList<BPPSupporterSample> _supporters = Array.Empty<BPPSupporterSample>();
    private IOverlayPanelHandle? _overlayHandle;
    private bool _initialized;

    public static bool IsVisible { get; private set; }

    private HistoryRunRecord? SelectedRun => _state.GetSelectedRun(FilteredRuns);

    private HistoryBattleRecord? SelectedBattle => _state.GetSelectedBattle();

    private HistoryBattleRecord? SelectedGhostBattle =>
        _state.GetSelectedGhostBattle(FilteredGhostBattles);

    private HistoryBattleRecord? ActiveSelectedBattle =>
        _state.SectionMode == HistorySectionMode.Ghost ? SelectedGhostBattle : SelectedBattle;

    private IReadOnlyList<HistoryBattleRecord> FilteredGhostBattles => GetFilteredGhostBattles();

    private IReadOnlyList<HistoryRunRecord> FilteredRuns => GetFilteredRuns();

    private void Awake()
    {
        EnsureInitialized();
    }

    internal void Configure(
        HistoryPanelDependencies dependencies,
        INativeCardPreviewHost nativeCardPreviewHost
    )
    {
        EnsureInitialized();
        _dependencies = dependencies ?? throw new ArgumentNullException(nameof(dependencies));
        _runtime = dependencies.Runtime;
        _dataService = dependencies.DataService;
        _replayService = dependencies.ReplayService;
        _nativeCardPreviewHost =
            nativeCardPreviewHost ?? throw new ArgumentNullException(nameof(nativeCardPreviewHost));
        _coordinator = new HistoryPanelCoordinator(
            _state,
            dependencies,
            RefreshUi,
            RefreshSelectedBattlePreview,
            SetHistoryVisible
        );
    }

    private void OnDisable()
    {
        // Route through the host so its open-panel state cannot desync; the unconditional
        // hide below also covers the not-open case (matching the historical force-hide).
        _overlayHandle?.RequestClose();
        IsVisible = false;
        StopPreviewRender();
        _battleBoardPreview?.Hide();
        SetUiVisible(false);
    }

    private void OnDestroy()
    {
        if (ReferenceEquals(Instance, this))
            Instance = null;

        _overlayHandle?.Dispose();
        _overlayHandle = null;
        _coordinator?.Dispose();
        DisposePreviewRenderer();
        _dependencies = null;
        DisposeUi();
    }

    // Lifecycle (scene change, combat gate, hotkey, escape) is owned by the Overlay Panel Host;
    // this tick only carries the panel's own per-frame content work.
    private void Tick(float dt, bool isVisible)
    {
        if (!isVisible)
            return;

        _coordinator?.Tick(Time.unscaledTime);
        PollPreviewHover();
    }

    private void SetHistoryVisible(bool visible)
    {
        if (visible)
            _overlayHandle?.RequestOpen();
        else
            _overlayHandle?.RequestClose();
    }

    private void OnOverlayOpen()
    {
        EnsureUi();
        _supporters = BPPSupporters.SampleMany(4);
        IsVisible = true;
        _coordinator?.OnPanelShown();
        SetUiVisible(true);
        RefreshUi();
    }

    private void OnOverlayClose()
    {
        IsVisible = false;
        _coordinator?.OnPanelHidden();
        DisposePreviewRenderer();
        SetUiVisible(false);
        RefreshUi();
    }

    internal static void OpenFromDockEntry()
    {
        if (Instance == null)
        {
            BppLog.ErrorEvent(
                HistoryPanelLogEvents.OpenFailed,
                HistoryPanelLogEvents.OpenReasonCode.Bind(
                    HistoryPanelOpenReasonCode.InstanceUnavailable
                )
            );
            return;
        }

        Instance.OpenFromDockEntryInternal();
    }

    internal static void RefreshLocalization()
    {
        if (Instance == null || !IsVisible)
            return;

        Instance.RefreshLocalizationInternal();
    }

    internal void OpenFromUiEntry()
    {
        OpenFromDockEntryInternal();
    }

    private void OpenFromDockEntryInternal()
    {
        EnsureInitialized();

        try
        {
            if (_overlayHandle == null)
            {
                BppLog.ErrorEvent(
                    HistoryPanelLogEvents.OpenFailed,
                    HistoryPanelLogEvents.OpenReasonCode.Bind(
                        HistoryPanelOpenReasonCode.OverlayHandleUnavailable
                    )
                );
                return;
            }

            var outcome = _overlayHandle.RequestOpen();
            if (outcome == OverlayRequestOutcome.SuppressedByCombat)
            {
                BppLog.DebugEvent(
                    HistoryPanelLogEvents.OpenSkipped,
                    () =>
                        [
                            HistoryPanelLogEvents.OpenReasonCode.Bind(
                                HistoryPanelOpenReasonCode.CombatActive
                            ),
                        ]
                );
            }
            else if (outcome == OverlayRequestOutcome.UnknownPanel)
            {
                BppLog.ErrorEvent(
                    HistoryPanelLogEvents.OpenFailed,
                    HistoryPanelLogEvents.OpenReasonCode.Bind(
                        HistoryPanelOpenReasonCode.UnknownPanel
                    )
                );
            }
        }
        catch (Exception ex)
        {
            BppLog.ErrorEvent(
                HistoryPanelLogEvents.OpenFailed,
                ex,
                HistoryPanelLogEvents.OpenReasonCode.Bind(
                    HistoryPanelOpenReasonCode.RequestException
                )
            );
        }
    }

    private void RefreshLocalizationInternal()
    {
        RefreshUi();
    }

    private void RefreshSelectedBattlePreview()
    {
        StopPreviewRender();

        if (!IsVisible)
        {
            _battleBoardPreview?.Hide();
            return;
        }

        EnsurePreviewRenderer();
        if (_battleBoardPreview == null)
            return;

        var previewData = BuildSelectedBattlePreviewData();
        _previewCoroutine = StartCoroutine(
            _battleBoardPreview.Render(previewData.Board, OnPreviewPhase)
        );
    }

    private HistoryBattlePreviewData BuildSelectedBattlePreviewData()
    {
        var activeSelectedBattle = ActiveSelectedBattle;
        if (
            _state.PreviewSelectionMode == PreviewSelectionMode.Battle
            && activeSelectedBattle != null
        )
        {
            var signature = $"battle:{activeSelectedBattle.BattleId}";
            return _state.SectionMode == HistorySectionMode.Ghost
                ? ResolveGhostPreviewData(activeSelectedBattle, signature)
                : HistoryBattlePreviewProjection.BuildOpponent(
                    activeSelectedBattle.Snapshots,
                    signature
                );
        }

        var runPreviewBattle = PickRunPreviewBattle(_state.Battles);
        if (runPreviewBattle != null)
        {
            return HistoryBattlePreviewProjection.BuildPlayer(
                runPreviewBattle.Snapshots,
                $"run:{SelectedRun?.RunId}:{runPreviewBattle.BattleId}"
            );
        }

        return HistoryBattlePreviewData.Empty;
    }

    // Ghost replay payload snapshots stay in the uploader's original perspective.
    // For the local "against me" view, our board is stored on the opponent side.
    private HistoryBattlePreviewData ResolveGhostPreviewData(
        HistoryBattleRecord battle,
        string signature
    )
    {
        if (battle.Source != HistoryBattleSource.Ghost)
            return HistoryBattlePreviewProjection.BuildOpponent(battle.Snapshots, signature);

        var replayDirectoryPath = _runtime?.CombatReplayDirectoryPath;
        if (string.IsNullOrWhiteSpace(replayDirectoryPath))
            return HistoryBattlePreviewProjection.BuildEmpty(signature);

        var ghostPayloadStore = new GhostBattlePayloadStore(
            GhostBattlePayloadStore.ResolveDirectory(replayDirectoryPath)
        );
        var ghostPayloadResult = ghostPayloadStore.LoadDetailed(battle.BattleId);
        if (ghostPayloadResult.Status == FileBackedPayloadLoadStatus.Invalid)
        {
            _payloadFailureLogGate.Report(
                battle.BattleId,
                ghostPayloadResult.Fingerprint ?? "unavailable",
                HistoryPanelPreviewPayloadReasonCode.PayloadInvalid,
                ghostPayloadResult.Exception
            );
            return HistoryBattlePreviewProjection.BuildEmpty(signature);
        }
        if (ghostPayloadResult.Status == FileBackedPayloadLoadStatus.Unreadable)
        {
            _payloadFailureLogGate.Report(
                battle.BattleId,
                ghostPayloadResult.Fingerprint ?? "unavailable",
                HistoryPanelPreviewPayloadReasonCode.PayloadUnreadable,
                ghostPayloadResult.Exception
            );
            return HistoryBattlePreviewProjection.BuildEmpty(signature);
        }

        if (
            ghostPayloadResult.Status == FileBackedPayloadLoadStatus.Missing
            || ghostPayloadResult.Status == FileBackedPayloadLoadStatus.Loaded
        )
            _payloadFailureLogGate.Clear(battle.BattleId);
        var ghostPayload = ghostPayloadResult.Payload;
        var snapshots = ghostPayload?.BattleManifest?.Snapshots;
        if (snapshots == null)
            return HistoryBattlePreviewProjection.BuildEmpty(signature);

        return HistoryBattlePreviewProjection.BuildOpponent(snapshots, signature);
    }

    private static HistoryBattleRecord? PickRunPreviewBattle(
        IReadOnlyList<HistoryBattleRecord> runBattles
    )
    {
        if (runBattles.Count == 0)
            return null;

        return runBattles
            .OrderByDescending(battle => battle.Day ?? int.MinValue)
            .ThenByDescending(battle => battle.Hour ?? int.MinValue)
            .ThenByDescending(battle => battle.RecordedAtUtc)
            .FirstOrDefault();
    }

    private void OnPreviewPhase(ItemBoardPreviewPhase phase)
    {
        switch (phase)
        {
            case ItemBoardPreviewPhase.Empty:
                SetPreviewStatus(HistoryPanelText.NoLocallyRenderableCards(), true);
                break;
            case ItemBoardPreviewPhase.InitFailed:
                SetPreviewStatus(HistoryPanelText.PreviewRendererInitFailed(), true);
                break;
            case ItemBoardPreviewPhase.Loading:
                SetPreviewStatus(HistoryPanelText.LoadingPreview(), true);
                break;
            case ItemBoardPreviewPhase.Done:
                SetPreviewStatus(null, false);
                break;
        }
    }

    private void StopPreviewRender()
    {
        _battleBoardPreview?.CancelPending();

        if (_previewCoroutine == null)
            return;

        StopCoroutine(_previewCoroutine);
        _previewCoroutine = null;
    }

    private void EnsurePreviewRenderer()
    {
        _battleBoardPreview ??= new BppItemBoardPreview(
            _nativeCardPreviewHost,
            new ItemBoardPreviewOptions
            {
                Layer = 30,
                SortingOrder = BppOverlaySorting.NativeCardPreview,
                LayoutMode = ItemBoardPreviewLayoutMode.SlotGrid,
                ShowHover = true,
                CardPreviewFailureReporter = HistoryPanelPreviewLogWriter.ReportCardPreview,
                HoverFailureReporter = TooltipCardPreviewLogWriter.Reporter,
                ItemBoardFailureReporter = HistoryPanelPreviewLogWriter.ReportItemBoard,
                // The ~2:1 preview container always lands in the board-cap regime, where the
                // default ratio reproduces the native board's frame interleaving; this panel
                // reads that as overlap, so it opts into full frame-border separation.
                SlotGridMaxHeightRatio =
                    ItemBoardPreviewOptions.FrameSeparationSlotGridMaxHeightRatio,
            }
        );
        if (_hasPreviewContainerBounds)
            ApplyPreviewContainerBounds(_previewContainerBounds);
    }

    // Translates a screen-space UI Toolkit container Rect into the preview surface knobs.
    // SlotGrid uses position/clip for placement and autoFitScale for the same board-fit height
    // cap as LiveBuildPanel. The container Rect already arrives in physical pixels (the view
    // scales worldBound by scaledPixelsPerPoint), so this is a direct mapping with no
    // resolution-dependent fudge factor. Returns true if the card scale or bounds changed so
    // the caller knows to re-render.
    private bool ApplyPreviewContainerBounds(Rect bounds)
    {
        if (_battleBoardPreview == null)
            return false;

        var autoFitScale = Mathf.Min(
            bounds.width / ItemBoardSocketLayout.NativeBoardWidth,
            bounds.height / ItemBoardSocketLayout.NativeBoardHeight
        );

        _battleBoardPreview.SetPosition(new Vector2(bounds.x, bounds.y));
        _battleBoardPreview.SetClipSize(new Vector2(bounds.width, bounds.height));
        var boundsChanged = _previewContainerBoundsChanged;
        _previewContainerBoundsChanged = false;
        return _battleBoardPreview.SetCardScale(autoFitScale) || boundsChanged;
    }

    private void DisposePreviewRenderer()
    {
        StopPreviewRender();
        _battleBoardPreview?.Dispose();
        _battleBoardPreview = null;
    }

    private void PollPreviewHover()
    {
        var mouse = Mouse.current;
        if (mouse == null)
            return;

        _battleBoardPreview?.PollHover(mouse.position.ReadValue());
    }

    private void EnsureInitialized()
    {
        if (_initialized)
            return;

        _initialized = true;
        Instance = this;
    }

    internal void AttachToOverlayHost(OverlayPanelHost overlayHost)
    {
        if (_overlayHandle != null)
            return;

        _overlayHandle = overlayHost.Register(
            new OverlayPanelRegistration(
                OverlayPanelId,
                BppHotkeyActionId.ToggleHistoryPanel,
                onOpen: OnOverlayOpen,
                onClose: OnOverlayClose,
                tick: Tick
            )
            {
                // Historical behavior: the panel stays open across non-combat scene changes.
                SceneChangeClose = SceneChangeClosePolicy.OnlyWhenInCombat,
                // Swallow the toggle entirely (open AND close) while the search box has focus.
                HotkeyGuard = () => !IsVisible || !IsTextInputFocused(),
                OnSceneChanged = OnOverlaySceneChanged,
            }
        );
    }

    private void OnOverlaySceneChanged()
    {
        // The preview renderer is scene-bound; it is lazily recreated by the next
        // RefreshSelectedBattlePreview when the panel stays open (non-combat scene change).
        DisposePreviewRenderer();
    }

    private IReadOnlyList<HistoryBattleRecord> GetFilteredGhostBattles()
    {
        return _coordinator?.GetFilteredGhostBattles() ?? Array.Empty<HistoryBattleRecord>();
    }

    private IReadOnlyList<HistoryRunRecord> GetFilteredRuns()
    {
        return _coordinator?.GetFilteredRuns() ?? Array.Empty<HistoryRunRecord>();
    }
}
