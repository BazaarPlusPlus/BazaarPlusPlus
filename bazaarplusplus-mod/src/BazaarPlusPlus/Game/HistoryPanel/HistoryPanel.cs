#nullable enable
using BazaarPlusPlus.Core.RunContext;
using BazaarPlusPlus.Game.CombatReplay;
using BazaarPlusPlus.Game.HistoryPanel.Data;
using BazaarPlusPlus.Game.Input;
using BazaarPlusPlus.Game.OverlayPanels;
using BazaarPlusPlus.Game.Supporters;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using TheBazaar;
using UnityEngine;

namespace BazaarPlusPlus.Game.HistoryPanel;

internal sealed partial class HistoryPanel : MonoBehaviour
{
    private const string OverlayPanelId = "HistoryPanel";

    internal static HistoryPanel? Instance { get; private set; }

    private readonly HistoryPanelState _state = new();
    private HistoryPanelDependencies? _dependencies;
    private HistoryPanelCoordinator? _coordinator;
    private IRunContext? _runContext;
    private IReadOnlyList<BPPSupporterSample> _supporters = Array.Empty<BPPSupporterSample>();
    private IOverlayPanelHandle? _overlayHandle;
    private bool _initialized;

    public static bool IsVisible { get; private set; }

    private HistoryBattleRecord? ActiveSelectedBattle =>
        HistoryPanelViewModelBuilder.ActiveSelectedBattle(_state, _coordinator);

    private bool ShowsBothBoards => HistoryPanelViewModelBuilder.ShowsBothBoards(_state);

    private void Awake()
    {
        EnsureInitialized();
    }

    internal void Configure(HistoryPanelDependencies dependencies)
    {
        EnsureInitialized();
        _dependencies = dependencies ?? throw new ArgumentNullException(nameof(dependencies));
        _runContext = dependencies.RunContext;
        _coordinator = new HistoryPanelCoordinator(
            _state,
            dependencies,
            RefreshUi,
            RefreshSelectedBattlePreview,
            new HistoryPanelReplayReturnPort(
                () => SetHistoryVisible(false),
                () => SetHistoryVisible(true),
                () => CombatReplayRuntime.Instance?.SavedReplays,
                () =>
                    SceneLoader.IsSceneLoaded(SceneID.HeroSelectScene)
                    && SceneLoader.ActiveScene == SceneID.HeroSelectScene
            )
        );
    }

    private void OnDisable()
    {
        // Only a disable drops a pending return from a replay; an ordinary close keeps it.
        _coordinator?.CancelReplayReturn();
        // Route through the host so its open-panel state cannot desync; the unconditional
        // hide below also covers the not-open case (matching the historical force-hide).
        _overlayHandle?.RequestClose();
        IsVisible = false;
        DisposeNativeHistoryBoards();
        SetUiVisible(false);
    }

    private void OnDestroy()
    {
        if (ReferenceEquals(Instance, this))
            Instance = null;

        _overlayHandle?.Dispose();
        _overlayHandle = null;
        _coordinator?.Dispose();
        DisposeNativeHistoryBoards();
        _dependencies = null;
        DisposeUi();
    }

    // Lifecycle (scene change, combat gate, hotkey, escape) is owned by the Overlay Panel Host;
    // this tick lets the coordinator return from an accepted replay and carries per-frame content
    // work.
    private void Tick(float dt, bool isVisible)
    {
        if (!isVisible)
            _coordinator?.TickHidden();

        if (!IsVisible)
            return;

        _coordinator?.Tick(Time.unscaledTime);
        FlushUi();
        _uiView?.Tick();
        _nativePlayerBoard?.Fit();
        _nativeOpponentBoard?.Fit();
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
        FlushUi();
    }

    private void OnOverlayClose()
    {
        IsVisible = false;
        _coordinator?.OnPanelHidden();
        DisposeNativeHistoryBoards();
        SetUiVisible(false);
        RefreshUi();
    }

    internal static void OpenFromDockEntry()
    {
        if (Instance == null)
        {
            BppLog.ErrorEvent(
                new BppLogEvent(BppLogFeatureScope.HistoryPanel, "history_panel.open.failed"),
                ("reason_code", HistoryPanelOpenReasonCode.InstanceUnavailable)
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

    private void OpenFromDockEntryInternal()
    {
        EnsureInitialized();

        try
        {
            if (_overlayHandle == null)
            {
                BppLog.ErrorEvent(
                    new BppLogEvent(BppLogFeatureScope.HistoryPanel, "history_panel.open.failed"),
                    ("reason_code", HistoryPanelOpenReasonCode.OverlayHandleUnavailable)
                );
                return;
            }

            switch (_overlayHandle.RequestOpen())
            {
                case OverlayRequestOutcome.Executed:
                case OverlayRequestOutcome.AlreadyInState:
                    break;
                case OverlayRequestOutcome.SuppressedByCombat:
                    LogOpenSkipped(HistoryPanelOpenReasonCode.CombatActive);
                    break;
                case OverlayRequestOutcome.Disabled:
                    LogOpenSkipped(HistoryPanelOpenReasonCode.PanelDisabled);
                    break;
                case OverlayRequestOutcome.OpenFailed:
                    LogOpenFailed(HistoryPanelOpenReasonCode.OpenFailed);
                    break;
                case OverlayRequestOutcome.Busy:
                    LogOpenFailed(HistoryPanelOpenReasonCode.OverlayHostBusy);
                    break;
                case OverlayRequestOutcome.UnknownPanel:
                default:
                    LogOpenFailed(HistoryPanelOpenReasonCode.UnknownPanel);
                    break;
            }
        }
        catch (Exception ex)
        {
            BppLog.ErrorEvent(
                new BppLogEvent(BppLogFeatureScope.HistoryPanel, "history_panel.open.failed"),
                ex,
                ("reason_code", HistoryPanelOpenReasonCode.RequestException)
            );
        }
    }

    private static void LogOpenSkipped(HistoryPanelOpenReasonCode reasonCode) =>
        BppLog.DebugEvent(
            new BppLogEvent(BppLogFeatureScope.HistoryPanel, "history_panel.open.skipped"),
            () => [("reason_code", reasonCode)]
        );

    private static void LogOpenFailed(HistoryPanelOpenReasonCode reasonCode) =>
        BppLog.ErrorEvent(
            new BppLogEvent(BppLogFeatureScope.HistoryPanel, "history_panel.open.failed"),
            ("reason_code", reasonCode)
        );

    private void RefreshLocalizationInternal()
    {
        RefreshUi();
    }

    private void RefreshSelectedBattlePreview() => RefreshNativeHistoryBoards();

    private void EnsureInitialized()
    {
        if (_initialized)
            return;

        _initialized = true;
        Instance = this;
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
                    OnOverlayOpen();
                    return true;
                },
                onClose: _ => OnOverlayClose(),
                tick: Tick
            )
            {
                // Historical behavior: the panel stays open across non-combat scene changes.
                SceneChangeClose = SceneChangeClosePolicy.OnlyWhenInCombat,
                OnSceneChanged = OnOverlaySceneChanged,
            },
            BppHotkeyActionId.ToggleHistoryPanel,
            // Swallow the toggle entirely (open AND close) while the search box has focus.
            hotkeyGuard: () => !IsVisible || !IsTextInputFocused()
        );
    }

    private void OnOverlaySceneChanged()
    {
        // Recreate owned views after a scene transition so native pool leases stay valid.
        DisposeNativeHistoryBoards();
        if (IsVisible)
            RefreshSelectedBattlePreview();
    }
}
