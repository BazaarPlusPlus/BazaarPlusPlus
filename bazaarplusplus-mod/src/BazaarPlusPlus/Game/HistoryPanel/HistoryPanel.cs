#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using BazaarPlusPlus.Game.HistoryPanel.Data;
using BazaarPlusPlus.Game.HistoryPanel.Storage;
using BazaarPlusPlus.Game.Input;
using BazaarPlusPlus.Game.OverlayPanels;
using BazaarPlusPlus.Game.Supporters;
using BazaarPlusPlus.GameInterop.ItemBoardPreview;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.UiTokens;
using TheBazaar;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using Coroutine = UnityEngine.Coroutine;

namespace BazaarPlusPlus.Game.HistoryPanel;

internal sealed partial class HistoryPanel : MonoBehaviour
{
    private const string ToggleHistoryPanelBindingPath = "<Keyboard>/f8";
    private const string OverlayPanelId = "HistoryPanel";
    private const int OverlaySortingBand = BppOverlaySorting.MainOverlayPanelBand;
    private static readonly HashSet<string> UiDiagnosticScenes = new(StringComparer.Ordinal)
    {
        "CollectionUIScene",
        "CollectionWheelScene",
        "ChestSelectScene",
    };

    internal static HistoryPanel? Instance { get; private set; }

    private readonly HistoryPanelState _state = new();
    private HistoryPanelDependencies? _dependencies;
    private HistoryPanelCoordinator? _coordinator;
    private HistoryPanelDataService _dataService = null!;
    private HistoryPanelReplayService _replayService = null!;
    private HistoryPanelPreviewSource? _previewSource;
    private BppItemBoardPreview? _battleBoardPreview;
    private IHistoryPanelRuntime? _runtime;
    private Coroutine? _previewCoroutine;
    private IReadOnlyList<BPPSupporterSample> _supporters = Array.Empty<BPPSupporterSample>();
    private string _lastSceneToken = string.Empty;
    private bool _initialized;
    private bool _uiFontPrewarmedForScene;

    public static bool IsVisible { get; private set; }

    private HistoryRunRecord? SelectedRun => _state.GetSelectedRun();

    private HistoryBattleRecord? SelectedBattle => _state.GetSelectedBattle();

    private HistoryBattleRecord? SelectedGhostBattle =>
        _state.GetSelectedGhostBattle(FilteredGhostBattles);

    private HistoryBattleRecord? ActiveSelectedBattle =>
        _sectionMode == HistorySectionMode.Ghost ? SelectedGhostBattle : SelectedBattle;

    private IReadOnlyList<HistoryBattleRecord> FilteredGhostBattles => GetFilteredGhostBattles();

    private List<HistoryRunRecord> _runs => _state.Runs;

    private List<HistoryBattleRecord> _battles => _state.Battles;

    private List<HistoryBattleRecord> _ghostBattles => _state.GhostBattles;

    private List<HistoryBattleRecord> _filteredGhostBattles => _state.FilteredGhostBattles;

    private int _selectedRunIndex
    {
        get => _state.SelectedRunIndex;
        set => _state.SelectedRunIndex = value;
    }

    private int _selectedBattleIndex
    {
        get => _state.SelectedBattleIndex;
        set => _state.SelectedBattleIndex = value;
    }

    private int _selectedGhostBattleIndex
    {
        get => _state.SelectedGhostBattleIndex;
        set => _state.SelectedGhostBattleIndex = value;
    }

    private GhostBattleFilter _ghostBattleFilter
    {
        get => _state.GhostBattleFilter;
        set => _state.GhostBattleFilter = value;
    }

    private string? _statusMessage
    {
        get => _state.StatusMessage;
        set
        {
            _state.StatusMessage = value;
            _state.DeleteRunConfirmationStatusActive = false;
        }
    }

    private PreviewSelectionMode _previewSelectionMode
    {
        get => _state.PreviewSelectionMode;
        set => _state.PreviewSelectionMode = value;
    }

    private HistorySectionMode _sectionMode
    {
        get => _state.SectionMode;
        set => _state.SectionMode = value;
    }

    private bool _replayActionInProgress
    {
        get => _state.ReplayActionInProgress;
        set => _state.ReplayActionInProgress = value;
    }

    private void Awake()
    {
        EnsureInitialized("Awake");
    }

    internal void Configure(HistoryPanelDependencies dependencies)
    {
        EnsureInitialized("Configure");
        _dependencies = dependencies ?? throw new ArgumentNullException(nameof(dependencies));
        _runtime = dependencies.Runtime;
        _dataService = dependencies.DataService;
        _replayService = dependencies.ReplayService;
        _previewSource = new HistoryPanelPreviewSource(_runtime);
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
        IsVisible = false;
        _coordinator?.OnPanelHidden();
        StopPreviewRender();
        _battleBoardPreview?.Hide();
        SetUiVisible(false);
    }

    private void OnDestroy()
    {
        if (ReferenceEquals(Instance, this))
            Instance = null;

        BppOverlayPanelMutex.Unregister(OverlayPanelId);
        _coordinator?.Dispose();
        DisposePreviewRenderer();
        _dependencies = null;
        DisposeUi();
    }

    private void Update()
    {
        DetectSceneChange();

        if (IsVisible && TheBazaar.Data.IsInCombat)
        {
            SetHistoryVisible(false);
            return;
        }

        if (IsVisible)
            _coordinator?.Tick(Time.unscaledTime);

        if (BppHotkeyService.WasPressedThisFrame(ToggleHistoryPanelBindingPath))
        {
            ToggleFromHotkey();
            return;
        }

        var keyboard = Keyboard.current;
        if (keyboard == null)
            return;

        if (!IsVisible)
            return;

        if (keyboard.escapeKey.wasPressedThisFrame)
        {
            SetHistoryVisible(false);
            return;
        }

        PollPreviewHover();
    }

    private void SetHistoryVisible(bool visible)
    {
        var wasVisible = IsVisible;
        if (visible && !wasVisible)
            BppOverlayPanelMutex.CloseOthers(OverlayPanelId, OverlaySortingBand);

        if (visible)
            EnsureUi();

        if (visible && !wasVisible)
            _supporters = BPPSupporters.SampleMany(4);

        IsVisible = visible;
        if (visible)
            _coordinator?.OnPanelShown();
        else
        {
            _coordinator?.OnPanelHidden();
            DisposePreviewRenderer();
        }

        SetUiVisible(visible);
        RefreshUi();
    }

    internal void ToggleFromHotkey()
    {
        EnsureInitialized("ToggleFromHotkey");

        if (!CanOpenHistoryReview())
            return;

        try
        {
            SetHistoryVisible(!IsVisible);
        }
        catch (Exception ex)
        {
            BppLog.Error("HistoryPanel", "ToggleFromHotkey failed", ex);
        }
    }

    internal static void OpenFromDockEntry()
    {
        if (Instance == null)
        {
            BppLog.Warn("HistoryPanel", "Dock entry requested while HistoryPanel is unavailable.");
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
        EnsureInitialized("OpenFromDockEntry");

        if (!CanOpenHistoryReview())
        {
            BppLog.Warn(
                "HistoryPanel",
                "Ignored History Review open request because combat is active."
            );
            return;
        }

        try
        {
            SetHistoryVisible(true);
        }
        catch (Exception ex)
        {
            BppLog.Error("HistoryPanel", "OpenFromDockEntry failed", ex);
        }
    }

    private void RefreshLocalizationInternal()
    {
        RefreshUi();
    }

    private bool CanOpenHistoryReview()
    {
        return HistoryPanelAccessPolicy.CanOpen(TheBazaar.Data.IsInCombat);
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
        if (_battleBoardPreview == null || _previewSource == null)
            return;

        var previewData = _previewSource.Build(
            _previewSelectionMode,
            _sectionMode,
            ActiveSelectedBattle,
            SelectedRun,
            _battles
        );
        _previewCoroutine = StartCoroutine(
            _battleBoardPreview.Render(previewData.Board, OnPreviewPhase)
        );
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
            new ItemBoardPreviewOptions
            {
                Layer = 30,
                SortingOrder = BppOverlaySorting.NativeCardPreview,
                LayoutMode = ItemBoardPreviewLayoutMode.SlotGrid,
                ShowHover = true,
                LogComponent = "HistoryPanelPreview",
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

    private void EnsureInitialized(string source)
    {
        if (_initialized)
            return;

        _initialized = true;
        Instance = this;
        _lastSceneToken = GetSceneToken(SceneManager.GetActiveScene());
        BppOverlayPanelMutex.Register(
            new BppOverlayPanelRegistration(
                OverlayPanelId,
                OverlaySortingBand,
                () => IsVisible,
                () => Instance?.SetHistoryVisible(false)
            )
        );
        PrewarmUiFontState($"init:{source}");
    }

    private void DetectSceneChange()
    {
        var currentSceneToken = GetSceneToken(SceneManager.GetActiveScene());
        if (string.Equals(currentSceneToken, _lastSceneToken, StringComparison.Ordinal))
            return;

        _lastSceneToken = currentSceneToken;
        _uiFontPrewarmedForScene = false;
        PrewarmUiFontState("scene-change");
        LogEventSystemDiagnostics(SceneManager.GetActiveScene());
        if (IsVisible && TheBazaar.Data.IsInCombat)
            SetHistoryVisible(false);

        DisposePreviewRenderer();
    }

    private IReadOnlyList<HistoryBattleRecord> GetFilteredGhostBattles()
    {
        return _coordinator?.GetFilteredGhostBattles() ?? _filteredGhostBattles;
    }

    private static string GetSceneToken(Scene scene)
    {
        return $"{scene.name}|{scene.path}|{scene.buildIndex}|{scene.isLoaded}";
    }

    private void PrewarmUiFontState(string reason)
    {
        if (_uiFontPrewarmedForScene)
            return;

        BppLog.Info(
            "HistoryPanel",
            $"[UiToolkit] PrewarmUiFontState noop reason={reason} scene='{_lastSceneToken}'."
        );
        _uiFontPrewarmedForScene = true;
    }

    private static void LogEventSystemDiagnostics(Scene scene)
    {
        if (!UiDiagnosticScenes.Contains(scene.name))
            return;

        try
        {
            var eventSystems = Resources.FindObjectsOfTypeAll<EventSystem>();
            if (eventSystems == null || eventSystems.Length == 0)
            {
                BppLog.Warn(
                    "HistoryPanel",
                    $"[Diag][EventSystem] scene='{GetSceneToken(scene)}' found no EventSystem instances."
                );
                return;
            }

            var summaries = eventSystems.Select(
                (eventSystem, index) => DescribeEventSystem(eventSystem, index)
            );
            var currentSummary = DescribeEventSystem(EventSystem.current, null);
            BppLog.Info(
                "HistoryPanel",
                $"[Diag][EventSystem] scene='{GetSceneToken(scene)}' count={eventSystems.Length} current={currentSummary} entries={string.Join(" || ", summaries)}"
            );
        }
        catch (Exception ex)
        {
            BppLog.Error("HistoryPanel", "[Diag][EventSystem] Enumeration failed", ex);
        }
    }

    private static string DescribeEventSystem(EventSystem? eventSystem, int? index)
    {
        var prefix = index.HasValue ? $"#{index.Value}:" : string.Empty;
        if (eventSystem == null)
            return $"{prefix}<null>";

        var modules = eventSystem
            .GetComponents<BaseInputModule>()
            .Select(module =>
                $"{module.GetType().Name}(enabled={module.enabled},active={module.isActiveAndEnabled})"
            );

        return $"{prefix}{eventSystem.GetType().Name}(name='{eventSystem.name}',activeSelf={eventSystem.gameObject.activeSelf},activeInHierarchy={eventSystem.gameObject.activeInHierarchy},enabled={eventSystem.enabled},isCurrent={ReferenceEquals(EventSystem.current, eventSystem)},scene='{eventSystem.gameObject.scene.name}',modules=[{string.Join(", ", modules)}])";
    }
}
