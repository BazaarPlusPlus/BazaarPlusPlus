#nullable enable
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BazaarPlusPlus.Game.HistoryPanel;

internal sealed partial class HistoryPanel
{
    private const int CanvasSortingOrder = 26;
    private const int PillMaxVisibleCharacters = 10;
    private const float PanelWidth = 1280f;
    private const float PanelHeight = 960f;
    private const float ListColumnWidth = 392f;
    private const float PreviewSectionHeight = 256f;
    private const float GhostFilterButtonHeight = 22f;

    private static Sprite? _roundedSprite;
    private static TMP_FontAsset? _uiFont;

    private readonly List<ListItemView> _runItemViews = new();
    private readonly List<ListItemView> _battleItemViews = new();
    private string? _lastRenderedRunListSignature;
    private string? _lastRenderedBattleListSignature;
    private int _lastRenderedRunSelectionIndex = -1;
    private int _lastRenderedBattleSelectionIndex = -1;
    private HistorySectionMode _lastRenderedBattleSectionMode = (HistorySectionMode)(-1);

    private GameObject? _canvasObject;
    private RectTransform? _panelRoot;
    private RectTransform? _runSectionPanel;
    private RectTransform? _ghostModeRoot;
    private RectTransform? _ghostBattleListContent;
    private RectTransform? _runsModeRoot;
    private RectTransform? _runListContent;
    private RectTransform? _runsBattleListContent;
    private TextMeshProUGUI? _runsBattleSectionSubtitle;
    private RawImage? _previewSurface;
    private TextMeshProUGUI? _previewStatusText;
    private TextMeshProUGUI? _previewDebugText;
    private TextMeshProUGUI? _countChipText;
    private TextMeshProUGUI? _battleChipText;
    private TextMeshProUGUI? _databaseChipText;
    private TextMeshProUGUI? _statusText;
    private TextMeshProUGUI? _runSectionTitle;
    private TextMeshProUGUI? _footerPrimaryText;
    private TextMeshProUGUI? _footerSecondaryText;
    private Button? _runsTabButton;
    private Image? _runsTabButtonBackground;
    private TextMeshProUGUI? _runsTabButtonLabel;
    private Button? _ghostTabButton;
    private Image? _ghostTabButtonBackground;
    private TextMeshProUGUI? _ghostTabButtonLabel;
    private Button? _syncGhostButton;
    private Image? _syncGhostButtonBackground;
    private TextMeshProUGUI? _syncGhostButtonLabel;
    private Button? _ghostFilterAllButton;
    private Image? _ghostFilterAllButtonBackground;
    private TextMeshProUGUI? _ghostFilterAllButtonLabel;
    private Button? _ghostFilterIWonButton;
    private Image? _ghostFilterIWonButtonBackground;
    private TextMeshProUGUI? _ghostFilterIWonButtonLabel;
    private Button? _ghostFilterILostButton;
    private Image? _ghostFilterILostButtonBackground;
    private TextMeshProUGUI? _ghostFilterILostButtonLabel;
    private Button? _dynamicPreviewButton;
    private Image? _dynamicPreviewButtonBackground;
    private TextMeshProUGUI? _dynamicPreviewButtonLabel;
    private Button? _replayButton;
    private Image? _replayButtonBackground;
    private TextMeshProUGUI? _replayButtonLabel;
    private Button? _deleteRunButton;
    private Image? _deleteRunButtonBackground;
    private TextMeshProUGUI? _deleteRunButtonLabel;

    private sealed class ListItemView
    {
        public int Index;
        public Image? Background;
    }

    private readonly struct BattlePalette
    {
        public BattlePalette(
            Color normal,
            Color selected,
            Color accent,
            Color badgeBg,
            Color badgeText
        )
        {
            Normal = normal;
            Selected = selected;
            Accent = accent;
            BadgeBg = badgeBg;
            BadgeText = badgeText;
        }

        public Color Normal { get; }
        public Color Selected { get; }
        public Color Accent { get; }
        public Color BadgeBg { get; }
        public Color BadgeText { get; }
    }

    private readonly struct HeroBadgeStyle
    {
        public HeroBadgeStyle(string shortCode, Color background, Color text)
        {
            ShortCode = shortCode;
            Background = background;
            Text = text;
        }

        public string ShortCode { get; }

        public Color Background { get; }

        public Color Text { get; }
    }

    private void EnsureUi()
    {
        if (_canvasObject != null)
            return;

        _canvasObject = new GameObject(
            "HistoryPanelCanvas",
            typeof(RectTransform),
            typeof(Canvas),
            typeof(CanvasScaler),
            typeof(GraphicRaycaster)
        );
        _canvasObject.transform.SetParent(transform, false);

        var canvas = _canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = CanvasSortingOrder;

        var scaler = _canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.55f;

        var canvasRect = (RectTransform)_canvasObject.transform;
        StretchToParent(canvasRect, 0f, 0f, 0f, 0f);

        var backdrop = CreateRect("Backdrop", _canvasObject.transform);
        StretchToParent(backdrop, 0f, 0f, 0f, 0f);
        var backdropImage = AddImage(backdrop.gameObject, new Color(0.02f, 0.03f, 0.05f, 0.82f));
        backdropImage.raycastTarget = true;

        _panelRoot = CreateRect("PanelRoot", _canvasObject.transform);
        _panelRoot.anchorMin = new Vector2(0.5f, 0.5f);
        _panelRoot.anchorMax = new Vector2(0.5f, 0.5f);
        _panelRoot.pivot = new Vector2(0.5f, 0.5f);
        _panelRoot.sizeDelta = new Vector2(PanelWidth, PanelHeight);
        var bg = AddImage(_panelRoot.gameObject, new Color(0.08f, 0.10f, 0.13f, 0.985f));
        bg.raycastTarget = true;

        var shadow = CreateRect("Shadow", _panelRoot);
        StretchToParent(shadow, -14f, -14f, -14f, -14f);
        shadow.SetAsFirstSibling();
        AddImage(shadow.gameObject, new Color(0.01f, 0.01f, 0.02f, 0.35f));

        var glow = CreateRect("HeaderGlow", _panelRoot);
        glow.anchorMin = new Vector2(0f, 1f);
        glow.anchorMax = new Vector2(1f, 1f);
        glow.pivot = new Vector2(0.5f, 1f);
        glow.sizeDelta = new Vector2(0f, 170f);
        AddImage(glow.gameObject, new Color(0.79f, 0.61f, 0.22f, 0.08f));

        BuildHeader();
        BuildContent();
        BuildFooter();
    }

    private void DisposeUi()
    {
        if (_canvasObject == null)
            return;

        Destroy(_canvasObject);
        _canvasObject = null;
        _panelRoot = null;
        _runSectionPanel = null;
        _ghostModeRoot = null;
        _ghostBattleListContent = null;
        _runsModeRoot = null;
        _runListContent = null;
        _runsBattleListContent = null;
        _runsBattleSectionSubtitle = null;
        _previewSurface = null;
        _previewStatusText = null;
        _previewDebugText = null;
        _countChipText = null;
        _battleChipText = null;
        _databaseChipText = null;
        _statusText = null;
        _runSectionTitle = null;
        _footerPrimaryText = null;
        _footerSecondaryText = null;
        _runsTabButton = null;
        _runsTabButtonBackground = null;
        _runsTabButtonLabel = null;
        _ghostTabButton = null;
        _ghostTabButtonBackground = null;
        _ghostTabButtonLabel = null;
        _syncGhostButton = null;
        _syncGhostButtonBackground = null;
        _syncGhostButtonLabel = null;
        _ghostFilterAllButton = null;
        _ghostFilterAllButtonBackground = null;
        _ghostFilterAllButtonLabel = null;
        _ghostFilterIWonButton = null;
        _ghostFilterIWonButtonBackground = null;
        _ghostFilterIWonButtonLabel = null;
        _ghostFilterILostButton = null;
        _ghostFilterILostButtonBackground = null;
        _ghostFilterILostButtonLabel = null;
        _dynamicPreviewButton = null;
        _dynamicPreviewButtonBackground = null;
        _dynamicPreviewButtonLabel = null;
        _replayButton = null;
        _replayButtonBackground = null;
        _replayButtonLabel = null;
        _deleteRunButton = null;
        _deleteRunButtonBackground = null;
        _deleteRunButtonLabel = null;
        _runItemViews.Clear();
        _battleItemViews.Clear();
        _lastRenderedRunListSignature = null;
        _lastRenderedBattleListSignature = null;
        _lastRenderedRunSelectionIndex = -1;
        _lastRenderedBattleSelectionIndex = -1;
        _lastRenderedBattleSectionMode = (HistorySectionMode)(-1);
    }

    private void SetUiVisible(bool visible)
    {
        if (_canvasObject != null && _canvasObject.activeSelf != visible)
            _canvasObject.SetActive(visible);
    }

    private void RefreshUi()
    {
        if (_panelRoot == null)
            return;

        var canReplaySelectedBattle = CanReplaySelectedBattle(out var replayUnavailableReason);

        if (_countChipText != null)
            _countChipText.text =
                _sectionMode == HistorySectionMode.Ghost
                    ? $"{FilteredGhostBattles.Count} Ghost"
                    : $"{_runs.Count} Runs";
        if (_battleChipText != null)
            _battleChipText.text =
                _sectionMode == HistorySectionMode.Ghost
                    ? $"{FilteredGhostBattles.Count} Battles"
                    : $"{_battles.Count} Battles";
        if (_databaseChipText != null)
            _databaseChipText.text = $"DB {GetDatabaseChipText()}";

        if (_runSectionTitle != null)
            _runSectionTitle.text = (
                _sectionMode == HistorySectionMode.Ghost ? "Ghost" : "Runs"
            ).ToUpperInvariant();

        if (_statusText != null)
        {
            _statusText.text = _statusMessage ?? string.Empty;
            _statusText.gameObject.SetActive(!string.IsNullOrWhiteSpace(_statusText.text));
        }

        if (_runSectionPanel != null)
            _runSectionPanel.gameObject.SetActive(_sectionMode != HistorySectionMode.Ghost);

        if (_ghostModeRoot != null)
            _ghostModeRoot.gameObject.SetActive(_sectionMode == HistorySectionMode.Ghost);

        if (_runsModeRoot != null)
        {
            _runsModeRoot.gameObject.SetActive(_sectionMode != HistorySectionMode.Ghost);
            if (_runsBattleSectionSubtitle != null)
            {
                _runsBattleSectionSubtitle.text =
                    SelectedRun == null
                        ? "Select a run to inspect its recorded battles."
                        : $"{SelectedRun.Hero} | {HistoryPanelFormatter.FormatDayOnly(SelectedRun.FinalDay)}";
            }
        }

        if (_footerPrimaryText != null)
        {
            _footerPrimaryText.text =
                ActiveSelectedBattle == null
                    ? "No battle selected"
                    : $"{HistoryPanelFormatter.FormatBattleResult(ActiveSelectedBattle)} | {HistoryPanelFormatter.FormatDayOnly(ActiveSelectedBattle.Day)} | {ActiveSelectedBattle.OpponentName ?? "Unknown Opponent"}";
        }

        if (_footerSecondaryText != null)
        {
            var selectedBattleTimestamp =
                ActiveSelectedBattle == null
                    ? null
                    : HistoryPanelFormatter.FormatTimestamp(ActiveSelectedBattle.RecordedAtUtc);
            var selectedBattleTimestampText = selectedBattleTimestamp ?? string.Empty;
            var battleSummary =
                ActiveSelectedBattle == null
                    ? "Select one battle to inspect it, then use Replay when you want to jump back into it."
                : canReplaySelectedBattle
                    ? string.IsNullOrWhiteSpace(ActiveSelectedBattle.SnapshotSummary)
                            ? selectedBattleTimestampText
                        : $"{selectedBattleTimestampText} | {ActiveSelectedBattle.SnapshotSummary}"
                : $"{selectedBattleTimestampText} | Replay unavailable: {replayUnavailableReason}";
            _footerSecondaryText.text = string.IsNullOrWhiteSpace(_statusMessage)
                ? battleSummary
                : $"{_statusMessage} | {battleSummary}";
        }

        if (_previewStatusText != null && ActiveSelectedBattle == null)
        {
            _previewStatusText.text = "Select a battle to preview its recorded cards.";
            _previewStatusText.gameObject.SetActive(true);
        }

        RefreshActionButton(
            _runsTabButton,
            _runsTabButtonBackground,
            _runsTabButtonLabel,
            true,
            _sectionMode == HistorySectionMode.Runs
                ? new Color(0.78f, 0.60f, 0.24f, 0.98f)
                : new Color(0.23f, 0.27f, 0.32f, 0.98f),
            new Color(0.92f, 0.72f, 0.30f, 1f),
            new Color(0.24f, 0.26f, 0.30f, 0.50f),
            _sectionMode == HistorySectionMode.Runs
                ? new Color(0.10f, 0.07f, 0.03f, 1f)
                : Color.white
        );
        RefreshActionButton(
            _ghostTabButton,
            _ghostTabButtonBackground,
            _ghostTabButtonLabel,
            true,
            _sectionMode == HistorySectionMode.Ghost
                ? new Color(0.78f, 0.60f, 0.24f, 0.98f)
                : new Color(0.23f, 0.27f, 0.32f, 0.98f),
            new Color(0.92f, 0.72f, 0.30f, 1f),
            new Color(0.24f, 0.26f, 0.30f, 0.50f),
            _sectionMode == HistorySectionMode.Ghost
                ? new Color(0.10f, 0.07f, 0.03f, 1f)
                : Color.white
        );
        RefreshActionButton(
            _syncGhostButton,
            _syncGhostButtonBackground,
            _syncGhostButtonLabel,
            _dataService.CanSyncGhostBattles && !_ghostSyncInProgress,
            new Color(0.23f, 0.27f, 0.32f, 0.98f),
            new Color(0.35f, 0.39f, 0.44f, 1f),
            new Color(0.24f, 0.26f, 0.30f, 0.50f),
            Color.white
        );
        if (_syncGhostButtonLabel != null)
            _syncGhostButtonLabel.text = _ghostSyncInProgress ? "Syncing..." : "Sync Ghost";
        RefreshGhostFilterButton(
            _ghostFilterAllButton,
            _ghostFilterAllButtonBackground,
            _ghostFilterAllButtonLabel,
            GhostBattleFilter.All
        );
        RefreshGhostFilterButton(
            _ghostFilterIWonButton,
            _ghostFilterIWonButtonBackground,
            _ghostFilterIWonButtonLabel,
            GhostBattleFilter.IWon
        );
        RefreshGhostFilterButton(
            _ghostFilterILostButton,
            _ghostFilterILostButtonBackground,
            _ghostFilterILostButtonLabel,
            GhostBattleFilter.ILost
        );

        if (_dynamicPreviewButtonLabel != null)
        {
            _dynamicPreviewButtonLabel.text = GetDynamicPreviewButtonLabel(
                HistoryPanelPreviewSettings.DynamicPreviewEnabled
            );
        }

        if (_replayButtonLabel != null)
        {
            _replayButtonLabel.text = _replayActionInProgress
                ? "Working..."
                : _replayService.GetReplayActionLabel(ActiveSelectedBattle);
        }

        RefreshActionButton(
            _dynamicPreviewButton,
            _dynamicPreviewButtonBackground,
            _dynamicPreviewButtonLabel,
            true,
            HistoryPanelPreviewSettings.DynamicPreviewEnabled
                ? new Color(0.30f, 0.47f, 0.29f, 0.98f)
                : new Color(0.23f, 0.27f, 0.32f, 0.98f),
            HistoryPanelPreviewSettings.DynamicPreviewEnabled
                ? new Color(0.39f, 0.59f, 0.37f, 1f)
                : new Color(0.35f, 0.39f, 0.44f, 1f),
            new Color(0.24f, 0.26f, 0.30f, 0.50f),
            Color.white
        );

        RefreshActionButton(
            _replayButton,
            _replayButtonBackground,
            _replayButtonLabel,
            canReplaySelectedBattle && !_replayActionInProgress,
            new Color(0.78f, 0.60f, 0.24f, 0.98f),
            new Color(0.92f, 0.72f, 0.30f, 1f),
            new Color(0.24f, 0.26f, 0.30f, 0.50f),
            new Color(0.10f, 0.07f, 0.03f, 1f)
        );

        var canDeleteSelectedRun = CanDeleteSelectedRun(out _);
        if (_deleteRunButtonLabel != null)
        {
            _deleteRunButtonLabel.text = GetDeleteRunButtonLabel(
                _sectionMode == HistorySectionMode.Runs
                    && SelectedRun != null
                    && IsDeleteRunConfirmationActive(SelectedRun.RunId)
            );
        }

        RefreshActionButton(
            _deleteRunButton,
            _deleteRunButtonBackground,
            _deleteRunButtonLabel,
            canDeleteSelectedRun,
            _sectionMode == HistorySectionMode.Runs
            && SelectedRun != null
            && IsDeleteRunConfirmationActive(SelectedRun.RunId)
                ? new Color(0.75f, 0.23f, 0.20f, 0.98f)
                : new Color(0.46f, 0.19f, 0.18f, 0.98f),
            new Color(0.86f, 0.29f, 0.25f, 1f),
            new Color(0.24f, 0.26f, 0.30f, 0.50f),
            new Color(1f, 0.95f, 0.94f, 1f)
        );
        RefreshListsIfNeeded();
    }
}
