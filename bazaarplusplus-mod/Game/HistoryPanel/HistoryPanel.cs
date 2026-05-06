#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using BazaarPlusPlus.Game.HistoryPanel.Ghost;
using BazaarPlusPlus.Game.Input;
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
    private HistoryPanelPreviewRenderer? _previewRenderer;
    private IHistoryPanelRuntime? _runtime;
    private Coroutine? _previewCoroutine;
    private float _previewDebugOverlayUntil;
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

    private System.Collections.Generic.List<HistoryRunRecord> _runs => _state.Runs;

    private System.Collections.Generic.List<HistoryBattleRecord> _battles => _state.Battles;

    private System.Collections.Generic.List<HistoryBattleRecord> _ghostBattles =>
        _state.GhostBattles;

    private System.Collections.Generic.List<HistoryBattleRecord> _filteredGhostBattles =>
        _state.FilteredGhostBattles;

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

    private string? _deleteRunConfirmationRunId
    {
        get => _state.DeleteRunConfirmationRunId;
        set => _state.DeleteRunConfirmationRunId = value;
    }

    private float _deleteRunConfirmationUntil
    {
        get => _state.DeleteRunConfirmationUntil;
        set => _state.DeleteRunConfirmationUntil = value;
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

    private bool _ghostSyncInProgress
    {
        get => _state.GhostSyncInProgress;
        set => _state.GhostSyncInProgress = value;
    }

    private bool _replayActionInProgress
    {
        get => _state.ReplayActionInProgress;
        set => _state.ReplayActionInProgress = value;
    }

    private bool _filteredGhostBattlesDirty
    {
        get => _state.FilteredGhostBattlesDirty;
        set => _state.FilteredGhostBattlesDirty = value;
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
        _previewRenderer?.Hide();
        SetUiVisible(false);
    }

    private void OnDestroy()
    {
        if (ReferenceEquals(Instance, this))
            Instance = null;

        _coordinator?.Dispose();
        DisposePreviewRenderer();
        _dependencies = null;
        DisposeUi();
    }

    private void Update()
    {
        DetectSceneChange();

        if (IsVisible && Data.IsInCombat)
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

        _previewRenderer?.RenderLiveFrame();
        UpdatePreviewUiTick(Time.unscaledTime < _previewDebugOverlayUntil);

        if (TryHandlePreviewDebugHotkeys(keyboard))
            return;

        if (keyboard.escapeKey.wasPressedThisFrame)
            SetHistoryVisible(false);
    }

    private void SetHistoryVisible(bool visible)
    {
        if (visible)
            EnsureUi();

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
        UpdatePreviewUiTick(Time.unscaledTime < _previewDebugOverlayUntil);
    }

    private bool CanOpenHistoryReview()
    {
        return HistoryPanelAccessPolicy.CanOpen(Data.IsInCombat);
    }

    private void RefreshSelectedBattlePreview()
    {
        StopPreviewRender();

        if (!IsVisible)
        {
            _previewRenderer?.Hide();
            return;
        }

        EnsurePreviewRenderer();
        if (_previewRenderer == null)
            return;

        var previewRequest = BuildPreviewRequest();
        _previewCoroutine = StartCoroutine(
            _previewRenderer.RenderPreview(
                previewRequest.RenderId,
                previewRequest.PreviewData,
                SetPreviewStatus,
                () => UpdatePreviewUiTick(Time.unscaledTime < _previewDebugOverlayUntil)
            )
        );
    }

    private void StopPreviewRender()
    {
        _previewRenderer?.CancelPending();

        if (_previewCoroutine == null)
            return;

        StopCoroutine(_previewCoroutine);
        _previewCoroutine = null;
    }

    private void EnsurePreviewRenderer()
    {
        _previewRenderer ??= new HistoryPanelPreviewRenderer();
    }

    private void DisposePreviewRenderer()
    {
        StopPreviewRender();
        _previewRenderer?.Dispose();
        _previewRenderer = null;
    }

    private void EnsureInitialized(string source)
    {
        if (_initialized)
            return;

        _initialized = true;
        Instance = this;
        _lastSceneToken = GetSceneToken(SceneManager.GetActiveScene());
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
        if (IsVisible && Data.IsInCombat)
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

    private bool TryHandlePreviewDebugHotkeys(Keyboard keyboard)
    {
        if (_previewRenderer == null)
            return false;

        var ctrlPressed = keyboard.leftCtrlKey.isPressed || keyboard.rightCtrlKey.isPressed;
        if (!ctrlPressed)
            return false;

        var positionStep =
            keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed ? 1f : 0.25f;
        var fovStep = keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed ? 5f : 1f;
        var scaleStep =
            keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed ? 0.1f : 0.025f;
        var altPressed = keyboard.leftAltKey.isPressed || keyboard.rightAltKey.isPressed;

        var handled = false;
        if (keyboard.leftArrowKey.wasPressedThisFrame)
            handled = _previewRenderer.NudgeBoardHorizontalOffset(-positionStep);
        else if (keyboard.rightArrowKey.wasPressedThisFrame)
            handled = _previewRenderer.NudgeBoardHorizontalOffset(positionStep);
        else if (keyboard.upArrowKey.wasPressedThisFrame)
            handled = _previewRenderer.NudgeCameraDepth(-positionStep);
        else if (keyboard.downArrowKey.wasPressedThisFrame)
            handled = _previewRenderer.NudgeCameraDepth(positionStep);
        else if (keyboard.pageUpKey.wasPressedThisFrame)
            handled = _previewRenderer.NudgeCameraVerticalCenter(positionStep);
        else if (keyboard.pageDownKey.wasPressedThisFrame)
            handled = _previewRenderer.NudgeCameraVerticalCenter(-positionStep);
        else if (keyboard.homeKey.wasPressedThisFrame || keyboard.qKey.wasPressedThisFrame)
            handled = altPressed
                ? _previewRenderer.NudgeCardHeightScale(scaleStep)
                : _previewRenderer.NudgeCardWidthScale(scaleStep);
        else if (keyboard.endKey.wasPressedThisFrame || keyboard.eKey.wasPressedThisFrame)
            handled = altPressed
                ? _previewRenderer.NudgeCardHeightScale(-scaleStep)
                : _previewRenderer.NudgeCardWidthScale(-scaleStep);
        else if (keyboard.leftBracketKey.wasPressedThisFrame)
            handled = _previewRenderer.NudgeCardSpacingX(-scaleStep);
        else if (keyboard.rightBracketKey.wasPressedThisFrame)
            handled = _previewRenderer.NudgeCardSpacingX(scaleStep);
        else if (keyboard.equalsKey.wasPressedThisFrame)
            handled = _previewRenderer.NudgeFieldOfView(fovStep);
        else if (keyboard.minusKey.wasPressedThisFrame)
            handled = _previewRenderer.NudgeFieldOfView(-fovStep);
        else if (keyboard.backspaceKey.wasPressedThisFrame)
            handled = _previewRenderer.ResetDebugTuning();

        if (!handled)
            return false;

        _statusMessage =
            $"{HistoryPanelText.PreviewTuneStatus(_previewRenderer.GetDebugSummary())} | {HistoryPanelText.PreviewTuneHelp()}";
        ShowPreviewDebugOverlay(_previewRenderer.GetDebugSummary());
        RefreshUi();
        RefreshSelectedBattlePreview();
        return true;
    }

    private void ShowPreviewDebugOverlay(string summary)
    {
        SetPreviewDebugText(summary, true);
        _previewDebugOverlayUntil = Time.unscaledTime + 6f;
    }

    private PreviewRequest BuildPreviewRequest()
    {
        if (_previewSelectionMode == PreviewSelectionMode.Battle && ActiveSelectedBattle != null)
        {
            var previewData =
                _sectionMode == HistorySectionMode.Ghost
                    ? ResolveGhostPreviewData(ActiveSelectedBattle)
                    : ActiveSelectedBattle.PreviewData.OpponentHandOnly();
            return new PreviewRequest($"battle:{ActiveSelectedBattle.BattleId}", previewData);
        }

        var runPreviewBattle = GetRunPreviewBattle();
        if (runPreviewBattle != null)
        {
            return new PreviewRequest(
                $"run:{SelectedRun?.RunId}:{runPreviewBattle.BattleId}",
                runPreviewBattle.PreviewData.PlayerHandOnly()
            );
        }

        return new PreviewRequest(null, null);
    }

    private HistoryBattlePreviewData ResolveGhostPreviewData(HistoryBattleRecord battle)
    {
        if (battle.Source != HistoryBattleSource.Ghost)
            return battle.PreviewData;

        if (battle.PreviewData.HasRenderableCards)
            return battle.PreviewData.PlayerHandOnly();

        var replayDirectoryPath = _runtime?.CombatReplayDirectoryPath;
        if (string.IsNullOrWhiteSpace(replayDirectoryPath))
            return battle.PreviewData.PlayerHandOnly();

        var ghostPayloadStore = new GhostBattlePayloadStore(
            BuildGhostBattlePayloadDirectoryPath(replayDirectoryPath)
        );
        var ghostPayload = ghostPayloadStore.Load(battle.BattleId);
        var snapshots = ghostPayload?.BattleManifest?.Snapshots;
        if (snapshots == null)
            return battle.PreviewData.PlayerHandOnly();

        // Ghost replay payload snapshots stay in the uploader's original perspective.
        // For the local "against me" view, our board is stored on the opponent side.
        return HistoryPanelRepository.BuildPreviewData(snapshots).OpponentHandOnly();
    }

    private static string BuildGhostBattlePayloadDirectoryPath(string replayDirectoryPath)
    {
        var parentDirectory = System.IO.Path.GetDirectoryName(replayDirectoryPath);
        return string.IsNullOrWhiteSpace(parentDirectory)
            ? System.IO.Path.Combine(replayDirectoryPath, "GhostBattlePayloads")
            : System.IO.Path.Combine(parentDirectory, "GhostBattlePayloads");
    }

    private HistoryBattleRecord? GetRunPreviewBattle()
    {
        if (_battles.Count == 0)
            return null;

        return _battles
            .OrderByDescending(battle => battle.Day ?? int.MinValue)
            .ThenByDescending(battle => battle.Hour ?? int.MinValue)
            .ThenByDescending(battle => battle.RecordedAtUtc)
            .FirstOrDefault();
    }

    private readonly struct PreviewRequest
    {
        public PreviewRequest(string? renderId, HistoryBattlePreviewData? previewData)
        {
            RenderId = renderId;
            PreviewData = previewData;
        }

        public string? RenderId { get; }

        public HistoryBattlePreviewData? PreviewData { get; }
    }
}
