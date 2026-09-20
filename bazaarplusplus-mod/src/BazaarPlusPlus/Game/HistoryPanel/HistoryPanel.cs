#nullable enable
using BazaarPlusPlus.Game.CombatReplay;
using BazaarPlusPlus.Game.HistoryPanel.Data;
using BazaarPlusPlus.Game.Input;
using BazaarPlusPlus.Game.OverlayPanels;
using BazaarPlusPlus.Game.Supporters;
using BazaarPlusPlus.Infrastructure;
using TheBazaar;
using UnityEngine;

namespace BazaarPlusPlus.Game.HistoryPanel;

internal sealed partial class HistoryPanel : MonoBehaviour
{
    private const string OverlayPanelId = "HistoryPanel";

    internal static HistoryPanel? Instance { get; private set; }

    private bool _returnAfterReplay;

    private readonly HistoryPanelState _state = new();
    private HistoryPanelDependencies? _dependencies;
    private HistoryPanelCoordinator? _coordinator;
    private IHistoryPanelRunState? _runState;
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

    internal void Configure(HistoryPanelDependencies dependencies)
    {
        EnsureInitialized();
        _dependencies = dependencies ?? throw new ArgumentNullException(nameof(dependencies));
        _runState = dependencies.RunState;
        _coordinator = new HistoryPanelCoordinator(
            _state,
            dependencies,
            RefreshUi,
            RefreshSelectedBattlePreview,
            OnReplayVisibilityChange
        );
    }

    private void OnDisable()
    {
        _returnAfterReplay = false;
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
    // this tick restores the accepted replay origin and carries per-frame content work.
    private void Tick(float dt, bool isVisible)
    {
        if (
            _returnAfterReplay
            && !isVisible
            && CombatReplayRuntime.Instance is { HasSavedReplaySession: false }
            && SceneLoader.IsSceneLoaded(SceneID.HeroSelectScene)
            && SceneLoader.ActiveScene == SceneID.HeroSelectScene
        )
            SetHistoryVisible(true);

        if (!IsVisible)
            return;

        _coordinator?.Tick(Time.unscaledTime);
        _uiView?.Tick();
        _nativePlayerBoard?.Fit();
        _nativeOpponentBoard?.Fit();
    }

    private void OnReplayVisibilityChange(bool visible)
    {
        _returnAfterReplay = !visible;
        SetHistoryVisible(visible);
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
        var resumeSelection = _returnAfterReplay;
        _returnAfterReplay = false;
        _coordinator?.OnPanelShown(resumeSelection);
        SetUiVisible(true);
        RefreshUi();
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

    private void RefreshSelectedBattlePreview() => RefreshNativeHistoryBoards();

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
        // Recreate owned views after a scene transition so native pool leases stay valid.
        DisposeNativeHistoryBoards();
        if (IsVisible)
            RefreshSelectedBattlePreview();
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
