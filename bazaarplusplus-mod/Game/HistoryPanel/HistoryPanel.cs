#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using BazaarPlusPlus.Game.Input;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using Coroutine = UnityEngine.Coroutine;

namespace BazaarPlusPlus.Game.HistoryPanel;

internal sealed partial class HistoryPanel : MonoBehaviour
{
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

    public static bool IsVisible { get; private set; }

    private HistoryRunRecord? SelectedRun =>
        _runs.Count == 0 ? null : _runs[Mathf.Clamp(_selectedRunIndex, 0, _runs.Count - 1)];

    private HistoryBattleRecord? SelectedBattle =>
        _battles.Count == 0
            ? null
            : _battles[Mathf.Clamp(_selectedBattleIndex, 0, _battles.Count - 1)];

    private HistoryBattleRecord? SelectedGhostBattle =>
        FilteredGhostBattles.Count == 0
            ? null
            : FilteredGhostBattles[
                Mathf.Clamp(_selectedGhostBattleIndex, 0, FilteredGhostBattles.Count - 1)
            ];

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
        set => _state.StatusMessage = value;
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
        _dependencies?.GhostSyncService?.Dispose();
        _dependencies = null;
        DisposeUi();
    }

    private void Update()
    {
        DetectSceneChange();

        if (IsVisible)
            _coordinator?.Tick(Time.unscaledTime);

        var keyboard = Keyboard.current;
        if (keyboard == null)
            return;

        if (!IsVisible)
            return;

        if (_previewDebugText != null)
            _previewDebugText.gameObject.SetActive(Time.unscaledTime < _previewDebugOverlayUntil);

        if (HistoryPanelPreviewSettings.DynamicPreviewEnabled)
            _previewRenderer?.RenderLiveFrame(_previewSurface);

        if (TryHandlePreviewDebugHotkeys(keyboard))
            return;

        if (keyboard.escapeKey.wasPressedThisFrame)
            SetHistoryVisible(false);
    }

    private void SetHistoryVisible(bool visible)
    {
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
                "Ignored History Review open request because community contribution is disabled or a live run is active."
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

    private bool CanOpenHistoryReview()
    {
        return HistoryPanelAccessPolicy.CanOpen(
            _runtime?.IsInGameRun == true,
            BazaarPlusPlus
                .Core
                .Runtime
                .BppRuntimeHost
                .Config
                .EnableCommunityContributionConfig
                ?.Value
                ?? false
        );
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
        if (_previewRenderer == null || _previewSurface == null || _previewStatusText == null)
            return;

        var previewRequest = BuildPreviewRequest();
        _previewCoroutine = StartCoroutine(
            _previewRenderer.RenderPreview(
                previewRequest.RenderId,
                previewRequest.PreviewData,
                _previewSurface,
                _previewStatusText
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
        EnsureUi();
        SetUiVisible(false);
    }

    private void DetectSceneChange()
    {
        var currentSceneToken = GetSceneToken(SceneManager.GetActiveScene());
        if (string.Equals(currentSceneToken, _lastSceneToken, StringComparison.Ordinal))
            return;

        _lastSceneToken = currentSceneToken;
        if (IsVisible && _runtime?.IsInGameRun == true)
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

    private void ToggleDynamicPreviewFromUi()
    {
        var enabled = HistoryPanelPreviewSettings.ToggleDynamicPreviewEnabled();
        _statusMessage = enabled ? "Dynamic preview enabled." : "Dynamic preview disabled.";
        RefreshUi();

        if (!IsVisible)
            return;

        if (enabled)
        {
            EnsurePreviewRenderer();
            _previewRenderer?.RenderLiveFrame(_previewSurface);
            return;
        }

        RefreshSelectedBattlePreview();
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
            "Preview tune: "
            + _previewRenderer.GetDebugSummary()
            + " | Ctrl+Left/Right board spacing, Ctrl+[ / ] card spacing, Ctrl+Up/Down zoom, Ctrl+PgUp/PgDn vertical, Ctrl+Q/E or Home/End card width, Ctrl+Alt+Q/E or Home/End card height, Ctrl+-/= FOV, Ctrl+Backspace reset.";
        ShowPreviewDebugOverlay(_previewRenderer.GetDebugSummary());
        RefreshUi();
        RefreshSelectedBattlePreview();
        return true;
    }

    private void ShowPreviewDebugOverlay(string summary)
    {
        if (_previewDebugText == null)
            return;

        _previewDebugText.text = summary;
        _previewDebugText.gameObject.SetActive(true);
        _previewDebugOverlayUntil = Time.unscaledTime + 6f;
    }

    private PreviewRequest BuildPreviewRequest()
    {
        if (_previewSelectionMode == PreviewSelectionMode.Battle && ActiveSelectedBattle != null)
        {
            return new PreviewRequest(
                $"battle:{ActiveSelectedBattle.BattleId}",
                ActiveSelectedBattle.PreviewData.OpponentHandOnly()
            );
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
