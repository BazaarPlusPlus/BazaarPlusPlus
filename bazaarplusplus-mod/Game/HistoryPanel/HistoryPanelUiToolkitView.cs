#nullable enable
using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace BazaarPlusPlus.Game.HistoryPanel;

internal sealed partial class HistoryPanelUiToolkitView : IDisposable
{
    private static Font? _uiFont;

    private readonly Transform _parent;
    private readonly Action _close;
    private readonly Action _replay;
    private readonly Action _delete;
    private readonly Action _refreshFinalBuilds;
    private readonly Action<int> _selectRun;
    private readonly Action<int> _selectBattle;
    private readonly Action<HistorySectionMode> _setSectionMode;
    private readonly Action<GhostBattleFilter> _setGhostFilter;

    private GameObject? _rootObject;
    private UIDocument? _document;
    private PanelSettings? _panelSettings;
    private VisualElement? _root;
    private Label? _title;
    private Label? _subtitle;
    private Label? _countChip;
    private Label? _battleChip;
    private Label? _databaseChip;
    private Button? _runsTabButton;
    private Button? _ghostTabButton;
    private Button? _finalBuildRefreshButton;
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
    private Label? _footerPrimary;
    private Label? _footerSecondary;
    private Button? _deleteButton;
    private Button? _replayButton;
    private bool _suppressSelectionCallbacks;

    public HistoryPanelUiToolkitView(
        Transform parent,
        Action close,
        Action replay,
        Action delete,
        Action refreshFinalBuilds,
        Action<int> selectRun,
        Action<int> selectBattle,
        Action<HistorySectionMode> setSectionMode,
        Action<GhostBattleFilter> setGhostFilter
    )
    {
        _parent = parent ?? throw new ArgumentNullException(nameof(parent));
        _close = close ?? throw new ArgumentNullException(nameof(close));
        _replay = replay ?? throw new ArgumentNullException(nameof(replay));
        _delete = delete ?? throw new ArgumentNullException(nameof(delete));
        _refreshFinalBuilds =
            refreshFinalBuilds ?? throw new ArgumentNullException(nameof(refreshFinalBuilds));
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
        _panelSettings.sortingOrder = 26;
        _panelSettings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
        _panelSettings.referenceResolution = new Vector2Int(1920, 1080);
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
        _root.style.unityFont = GetUiFont();
        _root.pickingMode = PickingMode.Position;

        BuildTree(_root);
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
        _subtitle!.text = model.Subtitle;
        _countChip!.text = model.CountChipText;
        _battleChip!.text = model.BattleChipText;
        _databaseChip!.text = model.DatabaseChipText;
        _statusLabel!.text = model.StatusMessage ?? string.Empty;
        _statusLabel.style.display = string.IsNullOrWhiteSpace(model.StatusMessage)
            ? DisplayStyle.None
            : DisplayStyle.Flex;
        _runsSection!.style.display =
            model.SectionMode == HistorySectionMode.Ghost ? DisplayStyle.None : DisplayStyle.Flex;
        _battlesSection!.style.marginLeft =
            model.SectionMode == HistorySectionMode.Ghost ? 0f : 18f;
        _battlesTitle!.style.display =
            model.SectionMode == HistorySectionMode.Ghost ? DisplayStyle.None : DisplayStyle.Flex;
        _runsBattleSubtitle!.text = model.RunsBattleSubtitle;
        _runsBattleSubtitle.style.display = DisplayStyle.None;
        _footerPrimary!.text = model.FooterPrimaryText;
        _footerSecondary!.text = model.FooterSecondaryText;
        _ghostOpponentEliminatedNotice!.text = model.GhostOpponentEliminatedNoticeText;
        _ghostOpponentEliminatedNotice.style.display =
            string.IsNullOrWhiteSpace(model.GhostOpponentEliminatedNoticeText)
                ? DisplayStyle.None
                : DisplayStyle.Flex;

        RefreshTabButton(_runsTabButton!, model.SectionMode == HistorySectionMode.Runs);
        RefreshTabButton(_ghostTabButton!, model.SectionMode == HistorySectionMode.Ghost);
        _finalBuildRefreshButton!.text = model.FinalBuildRefreshButtonText;
        _finalBuildRefreshButton.SetEnabled(model.FinalBuildRefreshButtonEnabled);
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
        _replayButton.SetEnabled(model.ReplayButtonEnabled);
        _deleteButton!.text = model.DeleteButtonText;
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
