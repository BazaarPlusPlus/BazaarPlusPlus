#nullable enable
using System;
using BazaarPlusPlus.Game.HistoryPanel.Data;
using BazaarPlusPlus.Game.Supporters;
using BazaarPlusPlus.Game.Supporters.Ui;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Fonts;
using BazaarPlusPlus.Infrastructure.UiTokens;
using UnityEngine;
using UnityEngine.UIElements;

namespace BazaarPlusPlus.Game.HistoryPanel.Ui;

internal sealed partial class HistoryPanelUiToolkitView : IDisposable
{
    private readonly Transform _parent;
    private readonly Action _close;
    private readonly Action _replay;
    private readonly Action _recordAndReplay;
    private readonly Action _delete;
    private readonly Action _checkServerHealth;
    private readonly Action<int> _selectRun;
    private readonly Action<int> _selectBattle;
    private readonly Action<HistorySectionMode> _setSectionMode;
    private readonly Action<GhostBattleFilter> _setGhostFilter;

    private GameObject? _rootObject;
    private UIDocument? _document;
    private PanelSettings? _panelSettings;
    private VisualElement? _root;
    private Label? _title;
    private VisualElement? _subtitle;
    private Label? _countChip;
    private Label? _battleChip;
    private Label? _databaseChip;
    private Button? _checkServerHealthButton;
    private Button? _runsTabButton;
    private Button? _ghostTabButton;
    private Label? _statusLabel;
    private VisualElement? _runsSection;
    private VisualElement? _battlesSection;
    private VisualElement? _ghostFilterRow;
    private Button? _ghostAllButton;
    private Button? _ghostWonButton;
    private Button? _ghostLostButton;
    private ListView? _runsList;
    private ListView? _battleList;
    private Label? _battlesTitle;
    private Label? _runsBattleSubtitle;
    private Label? _ghostOpponentEliminatedNotice;
    private Image? _previewImage;
    private Label? _previewStatusLabel;
    private Label? _previewDebugLabel;
    private VisualElement? _previewContainer;
    private Label? _detailTitle;
    private Label? _resultPill;
    private Label? _dayPill;
    private Label? _opponentName;
    private Label? _detailMeta;
    private Label? _detailSnapshot;
    private Label? _detailPlaceholder;
    private Button? _deleteButton;
    private Button? _replayButton;
    private Button? _recordAndReplayButton;
    private Button? _closeButton;
    private bool _suppressSelectionCallbacks;
    private Rect _lastPreviewContainerBounds;

    public event Action<Rect>? PreviewContainerBoundsChanged;

    public HistoryPanelUiToolkitView(
        Transform parent,
        Action close,
        Action replay,
        Action recordAndReplay,
        Action delete,
        Action checkServerHealth,
        Action<int> selectRun,
        Action<int> selectBattle,
        Action<HistorySectionMode> setSectionMode,
        Action<GhostBattleFilter> setGhostFilter
    )
    {
        _parent = parent ?? throw new ArgumentNullException(nameof(parent));
        _close = close ?? throw new ArgumentNullException(nameof(close));
        _replay = replay ?? throw new ArgumentNullException(nameof(replay));
        _recordAndReplay =
            recordAndReplay ?? throw new ArgumentNullException(nameof(recordAndReplay));
        _delete = delete ?? throw new ArgumentNullException(nameof(delete));
        _checkServerHealth =
            checkServerHealth ?? throw new ArgumentNullException(nameof(checkServerHealth));
        _selectRun = selectRun ?? throw new ArgumentNullException(nameof(selectRun));
        _selectBattle = selectBattle ?? throw new ArgumentNullException(nameof(selectBattle));
        _setSectionMode = setSectionMode ?? throw new ArgumentNullException(nameof(setSectionMode));
        _setGhostFilter = setGhostFilter ?? throw new ArgumentNullException(nameof(setGhostFilter));
    }

    public void EnsureCreated()
    {
        if (_rootObject != null)
            return;

        _rootObject = new GameObject("HistoryPanelUiToolkitRoot");
        _rootObject.transform.SetParent(_parent, false);
        _panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
        _panelSettings.sortingOrder = BppOverlaySorting.PanelUiToolkit;
        _panelSettings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
        _panelSettings.referenceResolution = new Vector2Int(1920, 1080);
        // Match by height (match=1): UI tokens scale with screen height so vertical density
        // stays constant and ultrawide screens no longer overflow vertically. The panel fills
        // the screen edge-to-edge regardless; match only governs how the px tokens scale.
        _panelSettings.match = 1f;
        _panelSettings.clearColor = false;
        _panelSettings.targetDisplay = 0;

        _document = _rootObject.AddComponent<UIDocument>();
        _document.panelSettings = _panelSettings;
        _root = _document.rootVisualElement;
        _root.style.flexGrow = 1f;
        _root.style.position = Position.Absolute;
        _root.style.left = 0f;
        _root.style.right = 0f;
        _root.style.top = 0f;
        _root.style.bottom = 0f;
        _root.style.display = DisplayStyle.None;
        var fontAtlasSample = HistoryPanelText.FontAtlasSample();
        BppUiFont.RequestCharactersInTexture(fontAtlasSample, Sizes.FontButton, FontStyle.Normal);
        BppUiFont.RequestCharactersInTexture(fontAtlasSample, Sizes.FontButton, FontStyle.Bold);
        _root.style.unityFont = GetUiFont();
        _root.pickingMode = PickingMode.Position;

        BuildTree(_root);

        _previewContainer?.RegisterCallback<GeometryChangedEvent>(
            OnPreviewContainerGeometryChanged
        );
    }

    private void OnPreviewContainerGeometryChanged(GeometryChangedEvent evt)
    {
        var worldBound = _previewContainer?.worldBound ?? evt.newRect;
        // worldBound is in UI Toolkit panel POINTS (the ~1920-wide reference space), not physical
        // pixels. The overlay Canvas works in physical pixels, so scale points -> pixels via
        // scaledPixelsPerPoint (1 at 1080p, 2 at 4K, …) and flip Y against the scaled top edge.
        // Without this the rect is correct only at 1080p (points == pixels); at 4K it lands
        // half-size in the upper-left.
        var ppp = _previewContainer?.scaledPixelsPerPoint ?? 1f;
        var bounds = new Rect(
            Mathf.Round(worldBound.x * ppp),
            Mathf.Round(Screen.height - worldBound.yMax * ppp),
            Mathf.Max(1f, Mathf.Round(worldBound.width * ppp)),
            Mathf.Max(1f, Mathf.Round(worldBound.height * ppp))
        );
        if (bounds.width <= 0f || bounds.height <= 0f)
            return;
        if (RectApproximately(_lastPreviewContainerBounds, bounds))
            return;

        _lastPreviewContainerBounds = bounds;
        PreviewContainerBoundsChanged?.Invoke(bounds);
    }

    private static bool RectApproximately(Rect left, Rect right)
    {
        return Mathf.Approximately(left.x, right.x)
            && Mathf.Approximately(left.y, right.y)
            && Mathf.Approximately(left.width, right.width)
            && Mathf.Approximately(left.height, right.height);
    }

    public void SetVisible(bool visible)
    {
        if (_root != null)
            _root.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
    }

    public void Refresh(HistoryPanelUiToolkitModel model)
    {
        if (_root == null || _runsList == null || _battleList == null)
            return;

        _title!.text = model.Title;
        _closeButton!.text = HistoryPanelText.Close();
        BPPSupporterAttributionRow.Bind(_subtitle!, model.Supporters, model.Subtitle);
        _countChip!.text = model.CountChipText;
        _battleChip!.text = model.BattleChipText;
        ApplyDatabaseChipSeverity(
            _databaseChip!,
            model.DatabaseChipText,
            model.DatabaseChipSeverity
        );
        _checkServerHealthButton!.text = model.ServerHealthButtonText;
        _checkServerHealthButton.SetEnabled(model.ServerHealthButtonEnabled);
        _statusLabel!.text = StablePanelText.Compact(model.StatusMessage, 180);
        _statusLabel.tooltip = model.StatusMessage ?? string.Empty;
        _statusLabel.style.display = string.IsNullOrWhiteSpace(model.StatusMessage)
            ? DisplayStyle.None
            : DisplayStyle.Flex;
        ApplyStatusSeverity(_statusLabel, model.StatusSeverity);
        _runsSection!.style.display =
            model.SectionMode == HistorySectionMode.Ghost ? DisplayStyle.None : DisplayStyle.Flex;
        _battlesSection!.style.marginLeft =
            model.SectionMode == HistorySectionMode.Ghost ? UiSpacing.None : UiSpacing.ColumnGap;
        _battlesTitle!.style.display =
            model.SectionMode == HistorySectionMode.Ghost ? DisplayStyle.None : DisplayStyle.Flex;
        _runsBattleSubtitle!.text = model.RunsBattleSubtitle;
        _runsBattleSubtitle.style.display = DisplayStyle.None;
        var hasSelection = model.HasSelectedBattle;
        _detailTitle!.style.display = hasSelection ? DisplayStyle.Flex : DisplayStyle.None;
        ConfigureResultPill(_resultPill!, model.DetailResultText, model.DetailResultSeverity);
        ConfigurePill(
            _dayPill!,
            model.DetailDayText,
            Colors.HistoryDayBubbleBackground,
            Colors.White,
            hasSelection && !string.IsNullOrWhiteSpace(model.DetailDayText)
        );
        _opponentName!.text = model.DetailOpponentName;
        _opponentName.tooltip = model.DetailOpponentName;
        _opponentName.style.display = hasSelection ? DisplayStyle.Flex : DisplayStyle.None;
        _detailMeta!.text = StablePanelText.Compact(model.DetailMetaText, 96);
        _detailMeta.tooltip = model.DetailMetaText;
        _detailMeta.style.display =
            hasSelection && !string.IsNullOrWhiteSpace(model.DetailMetaText)
                ? DisplayStyle.Flex
                : DisplayStyle.None;
        _detailSnapshot!.text = StablePanelText.Compact(model.DetailSnapshotText, 112);
        _detailSnapshot.tooltip = model.DetailSnapshotText;
        _detailSnapshot.style.display =
            hasSelection && !string.IsNullOrWhiteSpace(model.DetailSnapshotText)
                ? DisplayStyle.Flex
                : DisplayStyle.None;
        _detailPlaceholder!.text = StablePanelText.Compact(model.DetailPlaceholderText, 112);
        _detailPlaceholder.tooltip = model.DetailPlaceholderText;
        _detailPlaceholder.style.display = hasSelection ? DisplayStyle.None : DisplayStyle.Flex;
        _ghostOpponentEliminatedNotice!.text = StablePanelText.Compact(
            model.GhostOpponentEliminatedNoticeText,
            96
        );
        _ghostOpponentEliminatedNotice.tooltip = model.GhostOpponentEliminatedNoticeText;
        _ghostOpponentEliminatedNotice.style.display = string.IsNullOrWhiteSpace(
            model.GhostOpponentEliminatedNoticeText
        )
            ? DisplayStyle.None
            : DisplayStyle.Flex;

        RefreshTabButton(_runsTabButton!, model.SectionMode == HistorySectionMode.Runs);
        RefreshTabButton(_ghostTabButton!, model.SectionMode == HistorySectionMode.Ghost);
        _ghostFilterRow!.style.display =
            model.SectionMode == HistorySectionMode.Ghost ? DisplayStyle.Flex : DisplayStyle.None;
        RefreshGhostFilterButton(
            _ghostAllButton!,
            model.GhostBattleFilter == GhostBattleFilter.All
        );
        RefreshGhostFilterButton(
            _ghostWonButton!,
            model.GhostBattleFilter == GhostBattleFilter.IWon
        );
        RefreshGhostFilterButton(
            _ghostLostButton!,
            model.GhostBattleFilter == GhostBattleFilter.ILost
        );

        _replayButton!.text = model.ReplayButtonText;
        _replayButton.tooltip = model.ReplayButtonText;
        _replayButton.SetEnabled(model.ReplayButtonEnabled);
        _recordAndReplayButton!.text = model.RecordAndReplayButtonText;
        _recordAndReplayButton.tooltip = model.RecordAndReplayButtonText;
        _recordAndReplayButton.SetEnabled(model.RecordAndReplayButtonEnabled);
        _deleteButton!.text = model.DeleteButtonText;
        _deleteButton.tooltip = model.DeleteButtonText;
        _deleteButton.SetEnabled(model.DeleteButtonEnabled);
        RefreshDeleteButton(_deleteButton, model.DeleteButtonText, model.DeleteButtonEnabled);

        _runsList.itemsSource = model.Runs;
        _runsList.Rebuild();
        _battleList.itemsSource = model.VisibleBattles;
        _battleList.Rebuild();

        _suppressSelectionCallbacks = true;
        try
        {
            _runsList.selectedIndex = model.Runs.Count == 0 ? -1 : model.SelectedRunIndex;
            _battleList.selectedIndex =
                model.VisibleBattles.Count == 0 ? -1 : model.SelectedBattleIndex;
            _runsList.RefreshItems();
            _battleList.RefreshItems();
        }
        finally
        {
            _suppressSelectionCallbacks = false;
        }
    }

    public void SetPreviewTexture(Texture? texture)
    {
        if (_previewImage == null)
            return;

        _previewImage.image = texture;
        _previewImage.style.display = texture == null ? DisplayStyle.None : DisplayStyle.Flex;
        _previewImage.MarkDirtyRepaint();
    }

    public void SetPreviewStatus(string? message, bool visible)
    {
        if (_previewStatusLabel == null)
            return;

        _previewStatusLabel.text = message ?? string.Empty;
        _previewStatusLabel.style.display =
            visible && !string.IsNullOrWhiteSpace(message) ? DisplayStyle.Flex : DisplayStyle.None;
    }

    public void SetPreviewDebug(string? message, bool visible)
    {
        if (_previewDebugLabel == null)
            return;

        _previewDebugLabel.text = message ?? string.Empty;
        _previewDebugLabel.style.display =
            visible && !string.IsNullOrWhiteSpace(message) ? DisplayStyle.Flex : DisplayStyle.None;
    }

    public void SetPreviewDebugVisible(bool visible)
    {
        if (_previewDebugLabel == null)
            return;

        if (_previewDebugLabel.style.display != DisplayStyle.None)
            _previewDebugLabel.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
    }

    public void Dispose()
    {
        if (_rootObject != null)
            UnityEngine.Object.Destroy(_rootObject);

        if (_panelSettings != null)
            UnityEngine.Object.Destroy(_panelSettings);

        _rootObject = null;
        _document = null;
        _panelSettings = null;
        _root = null;
    }
}
