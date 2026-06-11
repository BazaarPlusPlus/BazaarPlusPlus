#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Game.LiveBuildPanel.Data;
using BazaarPlusPlus.Game.Supporters.Ui;
using BazaarPlusPlus.GameInterop.ItemBoardPreview;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Fonts;
using BazaarPlusPlus.Infrastructure.UiTokens;
using UnityEngine;
using UnityEngine.UIElements;

namespace BazaarPlusPlus.Game.LiveBuildPanel.Ui;

internal sealed class LiveBuildPanelView : IDisposable
{
    private const bool ShowSlotBackdrop = true;

    private sealed class RowElements
    {
        public Label Title = null!;
        public Label Empty = null!;
        public VisualElement SlotHost = null!;
        public readonly List<VisualElement> HitTargets = new();
        public readonly List<VisualElement> Markers = new();
    }

    private readonly Transform _parent;
    private readonly Action _close;
    private readonly Action _previous;
    private readonly Action _next;
    private readonly Action _refreshFinalBuilds;
    private readonly Dictionary<BppItemBoardId, RowElements> _rows = new();
    private GameObject? _rootObject;
    private UIDocument? _document;
    private PanelSettings? _panelSettings;
    private GameObject? _foregroundRootObject;
    private UIDocument? _foregroundDocument;
    private PanelSettings? _foregroundPanelSettings;
    private VisualElement? _foregroundRoot;
    private VisualElement? _root;
    private Label? _title;
    private VisualElement? _subtitle;
    private Label? _candidateCount;
    private Label? _corpusCardTitle;
    private Button? _finalBuildRefreshButton;
    private Label? _corpusStatus;
    private Label? _resultCardTitle;
    private Label? _recommendationStatus;
    private Button? _previousButton;
    private Button? _nextButton;
    private Button? _closeButton;

    public LiveBuildPanelView(
        Transform parent,
        Action close,
        Action previous,
        Action next,
        Action refreshFinalBuilds
    )
    {
        _parent = parent ?? throw new ArgumentNullException(nameof(parent));
        _close = close ?? throw new ArgumentNullException(nameof(close));
        _previous = previous ?? throw new ArgumentNullException(nameof(previous));
        _next = next ?? throw new ArgumentNullException(nameof(next));
        _refreshFinalBuilds =
            refreshFinalBuilds ?? throw new ArgumentNullException(nameof(refreshFinalBuilds));
    }

    public event Action<BppItemBoardId, Rect>? RowBoundsChanged;
    public event Action<BppItemBoardId, Guid>? CandidateToggleRequested;

    public void EnsureCreated()
    {
        if (_rootObject != null)
            return;

        _rootObject = new GameObject("LiveBuildPanelUiToolkitRoot");
        _rootObject.transform.SetParent(_parent, false);
        _panelSettings = CreatePanelSettings(BppOverlaySorting.PanelUiToolkit);

        _document = _rootObject.AddComponent<UIDocument>();
        _document.panelSettings = _panelSettings;
        _root = _document.rootVisualElement;
        ConfigureDocumentRoot(_root, PickingMode.Position);
        _root.style.unityFont = BppUiFont.Default;

        _foregroundRootObject = new GameObject("LiveBuildPanelForegroundUiToolkitRoot");
        _foregroundRootObject.transform.SetParent(_parent, false);
        _foregroundPanelSettings = CreatePanelSettings(BppOverlaySorting.PanelForeground);
        _foregroundDocument = _foregroundRootObject.AddComponent<UIDocument>();
        _foregroundDocument.panelSettings = _foregroundPanelSettings;
        _foregroundRoot = _foregroundDocument.rootVisualElement;
        ConfigureDocumentRoot(_foregroundRoot, PickingMode.Ignore);
        _foregroundRoot.style.unityFont = BppUiFont.Default;

        BppUiFont.RequestCharactersInTexture(
            LiveBuildPanelText.FontAtlasSample(),
            Sizes.FontButton,
            FontStyle.Normal
        );
        BuildTree(_root);
    }

    public void SetVisible(bool visible)
    {
        if (_root != null)
            _root.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        if (_foregroundRoot != null)
            _foregroundRoot.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
    }

    public void Refresh(LiveBuildPanelSnapshot snapshot)
    {
        if (_root == null)
            return;

        _title!.text = LiveBuildPanelText.Title();
        _closeButton!.text = LiveBuildPanelText.Close();
        BPPSupporterAttributionRow.Bind(
            _subtitle!,
            snapshot.Supporters,
            LiveBuildPanelText.Subtitle()
        );
        _candidateCount!.text = LiveBuildPanelText.CandidateCount(
            snapshot.CandidateTemplateIds.Count
        );
        _candidateCount.tooltip = _candidateCount.text;
        _corpusCardTitle!.text = LiveBuildPanelText.CorpusCardTitle();
        _finalBuildRefreshButton!.text = snapshot.FinalBuildRefreshButtonText;
        _finalBuildRefreshButton.tooltip = snapshot.FinalBuildRefreshButtonText;
        _finalBuildRefreshButton.SetEnabled(snapshot.FinalBuildRefreshButtonEnabled);
        _corpusStatus!.text = StablePanelText.Compact(snapshot.CorpusStatusText, 96);
        _corpusStatus.tooltip = string.IsNullOrWhiteSpace(snapshot.CorpusStatusTooltip)
            ? snapshot.CorpusStatusText
            : snapshot.CorpusStatusTooltip;
        _corpusStatus.style.color = ResolveRefreshStatusColor(snapshot.CorpusStatusSeverity);
        _resultCardTitle!.text = LiveBuildPanelText.ResultCardTitle();
        _recommendationStatus!.text = StablePanelText.Compact(snapshot.RecommendationStatus, 96);
        _recommendationStatus.tooltip = snapshot.RecommendationStatus;
        _previousButton!.text = LiveBuildPanelText.Previous();
        _previousButton.tooltip = LiveBuildPanelText.Previous();
        _nextButton!.text = LiveBuildPanelText.Next();
        _nextButton.tooltip = LiveBuildPanelText.Next();
        _previousButton.SetEnabled(snapshot.RecommendationCount > 1);
        _nextButton.SetEnabled(snapshot.RecommendationCount > 1);

        var candidates = new HashSet<Guid>(snapshot.CandidateTemplateIds);
        foreach (var row in snapshot.Rows)
            RefreshRow(row, candidates);
    }

    public void Dispose()
    {
        if (_rootObject != null)
            UnityEngine.Object.Destroy(_rootObject);
        if (_panelSettings != null)
            UnityEngine.Object.Destroy(_panelSettings);
        if (_foregroundRootObject != null)
            UnityEngine.Object.Destroy(_foregroundRootObject);
        if (_foregroundPanelSettings != null)
            UnityEngine.Object.Destroy(_foregroundPanelSettings);

        _rows.Clear();
        _rootObject = null;
        _document = null;
        _panelSettings = null;
        _foregroundRootObject = null;
        _foregroundDocument = null;
        _foregroundPanelSettings = null;
        _foregroundRoot = null;
        _root = null;
    }

    private void BuildTree(VisualElement root)
    {
        var panel = new VisualElement();
        panel.style.flexGrow = 1f;
        panel.style.minHeight = 0f;
        panel.style.flexDirection = FlexDirection.Row;
        panel.style.backgroundColor = Colors.HistoryPanelBackground;
        panel.style.paddingLeft = 34f;
        panel.style.paddingRight = 34f;
        panel.style.paddingTop = 28f;
        panel.style.paddingBottom = 28f;
        root.Add(panel);

        var boardArea = new VisualElement();
        boardArea.style.flexGrow = 1f;
        boardArea.style.flexShrink = 1f;
        boardArea.style.minWidth = 0f;
        boardArea.style.minHeight = 0f;
        boardArea.style.flexDirection = FlexDirection.Column;
        panel.Add(boardArea);

        foreach (
            var id in new[]
            {
                BppItemBoardId.FinalBuild,
                BppItemBoardId.LiveShop,
                BppItemBoardId.LiveBoard,
                BppItemBoardId.LiveStash,
            }
        )
        {
            BuildRow(boardArea, id);
        }

        BuildRail(panel);
    }

    private void BuildRow(VisualElement parent, BppItemBoardId id)
    {
        var row = new VisualElement();
        row.style.flexGrow = 1f;
        row.style.flexShrink = 1f;
        row.style.minHeight = 0f;
        row.style.marginBottom = 12f;
        row.style.backgroundColor = Colors.HistoryPreviewBackground;
        row.style.borderBottomColor = Colors.HistoryListFrameBorder;
        row.style.borderTopColor = Colors.HistoryListFrameBorder;
        row.style.borderLeftColor = Colors.HistoryListFrameBorder;
        row.style.borderRightColor = Colors.HistoryListFrameBorder;
        row.style.borderBottomWidth = 1f;
        row.style.borderTopWidth = 1f;
        row.style.borderLeftWidth = 1f;
        row.style.borderRightWidth = 1f;
        row.style.flexDirection = FlexDirection.Row;
        row.style.overflow = Overflow.Hidden;
        parent.Add(row);

        var labelColumn = new VisualElement();
        labelColumn.style.width = 160f;
        labelColumn.style.flexShrink = 0f;
        labelColumn.style.paddingLeft = 14f;
        labelColumn.style.paddingRight = 12f;
        labelColumn.style.justifyContent = Justify.Center;
        labelColumn.style.overflow = Overflow.Hidden;
        row.Add(labelColumn);

        var title = CreateLabel(18, FontStyle.Bold, Colors.HistorySectionTitleText);
        title.style.whiteSpace = WhiteSpace.NoWrap;
        title.style.overflow = Overflow.Hidden;
        labelColumn.Add(title);

        var empty = CreateLabel(13, FontStyle.Normal, Colors.HistoryFooterSecondaryText);
        empty.style.marginTop = 4f;
        empty.style.whiteSpace = WhiteSpace.Normal;
        empty.style.maxHeight = Sizes.LiveBuildRowEmptyMaxHeight;
        empty.style.overflow = Overflow.Hidden;
        labelColumn.Add(empty);

        var slotHost = new VisualElement();
        slotHost.style.flexGrow = 1f;
        slotHost.style.flexShrink = 1f;
        slotHost.style.minWidth = 0f;
        slotHost.style.position = Position.Relative;
        slotHost.style.overflow = Overflow.Hidden;
        row.Add(slotHost);

        if (ShowSlotBackdrop)
        {
            for (var i = 0; i < ItemBoardSlotGridGeometry.SocketCount; i++)
            {
                var slotRect = ItemBoardSlotGridGeometry.ResolveOccupiedRect(
                    100f,
                    100f,
                    i,
                    1,
                    0f,
                    0f
                );
                var slot = new VisualElement();
                slot.pickingMode = PickingMode.Ignore;
                slot.style.position = Position.Absolute;
                slot.style.left = Length.Percent(slotRect.X);
                slot.style.top = 6f;
                slot.style.bottom = 6f;
                slot.style.width = Length.Percent(slotRect.Width);
                slot.style.backgroundColor = Colors.CollectionSlotBackground;
                slot.style.borderLeftColor = Colors.HistoryListFrameBorder;
                slot.style.borderLeftWidth = i == 0 ? 0f : 1f;
                slotHost.Add(slot);
            }
        }

        slotHost.RegisterCallback<GeometryChangedEvent>(_ => PublishRowBounds(id, slotHost));
        _rows[id] = new RowElements
        {
            Title = title,
            Empty = empty,
            SlotHost = slotHost,
        };
    }

    private void BuildRail(VisualElement parent)
    {
        var rail = new VisualElement();
        rail.style.width = 330f;
        rail.style.flexShrink = 0f;
        rail.style.marginLeft = 24f;
        rail.style.minHeight = 0f;
        rail.style.overflow = Overflow.Hidden;
        rail.style.flexDirection = FlexDirection.Column;
        parent.Add(rail);

        var titleRow = new VisualElement();
        titleRow.style.flexDirection = FlexDirection.Row;
        titleRow.style.alignItems = Align.Center;
        rail.Add(titleRow);

        _title = CreateLabel(28, FontStyle.Bold, Colors.HistoryTitleText);
        _title.style.flexGrow = 1f;
        _title.style.flexShrink = 1f;
        _title.style.minWidth = 0f;
        _title.style.whiteSpace = WhiteSpace.NoWrap;
        _title.style.overflow = Overflow.Hidden;
        titleRow.Add(_title);

        _closeButton = CreateButton(LiveBuildPanelText.Close(), _close);
        _closeButton.style.width = 86f;
        _closeButton.style.backgroundColor = Colors.CloseBackground;
        _closeButton.style.color = Colors.CloseText;
        titleRow.Add(_closeButton);

        _subtitle = BPPSupporterAttributionRow.Create();
        _subtitle.style.marginTop = 8f;
        rail.Add(_subtitle);

        _candidateCount = CreateLabel(16, FontStyle.Bold, Colors.HistoryChipText);
        _candidateCount.style.marginTop = 22f;
        _candidateCount.style.height = 34f;
        _candidateCount.style.whiteSpace = WhiteSpace.NoWrap;
        _candidateCount.style.overflow = Overflow.Hidden;
        _candidateCount.style.backgroundColor = Colors.HistoryChipBackground;
        _candidateCount.style.unityTextAlign = TextAnchor.MiddleCenter;
        rail.Add(_candidateCount);

        // Corpus card: pull action + corpus state in one block. Fixed height on purpose — the
        // body swaps copy in place (pending/failure/guidance/summary) instead of showing/hiding
        // sibling boxes, so the rail never reflows on status changes. Refresh feedback stays in
        // the rail (never a board-row empty text): row copy feeds geometry callbacks and would
        // re-trigger preview redraws on every status change.
        var corpusCard = new VisualElement();
        corpusCard.style.marginTop = 12f;
        corpusCard.style.height = Sizes.LiveBuildCorpusCardHeight;
        corpusCard.style.minHeight = Sizes.LiveBuildCorpusCardHeight;
        corpusCard.style.maxHeight = Sizes.LiveBuildCorpusCardHeight;
        corpusCard.style.backgroundColor = Colors.HistoryStatusBackground;
        corpusCard.style.paddingLeft = 12f;
        corpusCard.style.paddingRight = 10f;
        corpusCard.style.paddingTop = 10f;
        corpusCard.style.paddingBottom = 10f;
        corpusCard.style.overflow = Overflow.Hidden;
        rail.Add(corpusCard);

        var corpusHeader = new VisualElement();
        corpusHeader.style.flexDirection = FlexDirection.Row;
        corpusHeader.style.alignItems = Align.Center;
        corpusCard.Add(corpusHeader);

        _corpusCardTitle = CreateLabel(15, FontStyle.Bold, Colors.HistorySectionTitleText);
        _corpusCardTitle.style.flexGrow = 1f;
        _corpusCardTitle.style.flexShrink = 1f;
        _corpusCardTitle.style.minWidth = 0f;
        _corpusCardTitle.style.whiteSpace = WhiteSpace.NoWrap;
        _corpusCardTitle.style.overflow = Overflow.Hidden;
        corpusHeader.Add(_corpusCardTitle);

        _finalBuildRefreshButton = CreateButton(
            LiveBuildPanelText.RefreshFinalBuilds(),
            _refreshFinalBuilds
        );
        _finalBuildRefreshButton.style.marginLeft = 8f;
        _finalBuildRefreshButton.style.width = Sizes.LiveBuildRefreshButtonWidth;
        _finalBuildRefreshButton.style.minWidth = Sizes.LiveBuildRefreshButtonWidth;
        _finalBuildRefreshButton.style.maxWidth = Sizes.LiveBuildRefreshButtonWidth;
        _finalBuildRefreshButton.style.height = Sizes.LiveBuildRefreshButtonHeight;
        _finalBuildRefreshButton.style.minHeight = Sizes.LiveBuildRefreshButtonHeight;
        _finalBuildRefreshButton.style.maxHeight = Sizes.LiveBuildRefreshButtonHeight;
        _finalBuildRefreshButton.style.flexGrow = 0f;
        _finalBuildRefreshButton.style.flexShrink = 0f;
        corpusHeader.Add(_finalBuildRefreshButton);

        _corpusStatus = CreateLabel(13, FontStyle.Normal, Colors.HistoryStatusText);
        _corpusStatus.style.marginTop = 8f;
        _corpusStatus.style.whiteSpace = WhiteSpace.Normal;
        _corpusStatus.style.maxHeight = Sizes.LiveBuildCorpusStatusMaxHeight;
        _corpusStatus.style.overflow = Overflow.Hidden;
        corpusCard.Add(_corpusStatus);

        // Result card: match status + recommendation paging, visually mirroring the corpus card
        // so the rail reads as "data in, results out".
        var resultCard = new VisualElement();
        resultCard.style.marginTop = 12f;
        resultCard.style.backgroundColor = Colors.HistoryStatusBackground;
        resultCard.style.paddingLeft = 12f;
        resultCard.style.paddingRight = 12f;
        resultCard.style.paddingTop = 10f;
        resultCard.style.paddingBottom = 10f;
        resultCard.style.overflow = Overflow.Hidden;
        rail.Add(resultCard);

        _resultCardTitle = CreateLabel(15, FontStyle.Bold, Colors.HistorySectionTitleText);
        _resultCardTitle.style.whiteSpace = WhiteSpace.NoWrap;
        _resultCardTitle.style.overflow = Overflow.Hidden;
        resultCard.Add(_resultCardTitle);

        _recommendationStatus = CreateLabel(15, FontStyle.Normal, Colors.HistoryStatusText);
        _recommendationStatus.style.marginTop = 8f;
        _recommendationStatus.style.whiteSpace = WhiteSpace.Normal;
        _recommendationStatus.style.maxHeight = Sizes.LiveBuildRecommendationStatusMaxHeight;
        _recommendationStatus.style.overflow = Overflow.Hidden;
        resultCard.Add(_recommendationStatus);

        var nav = new VisualElement();
        nav.style.flexDirection = FlexDirection.Row;
        nav.style.marginTop = 10f;
        resultCard.Add(nav);

        _previousButton = CreateButton(LiveBuildPanelText.Previous(), _previous);
        _previousButton.style.flexGrow = 1f;
        nav.Add(_previousButton);

        _nextButton = CreateButton(LiveBuildPanelText.Next(), _next);
        _nextButton.style.flexGrow = 1f;
        _nextButton.style.marginLeft = 8f;
        nav.Add(_nextButton);
    }

    private void RefreshRow(LiveItemBoardRowVm row, HashSet<Guid> candidates)
    {
        if (!_rows.TryGetValue(row.Board.Id, out var elements))
            return;

        elements.Title.text = row.Title;
        elements.Title.tooltip = row.Title;
        if (row.Board.Cards.Count == 0)
        {
            elements.Empty.text = StablePanelText.Compact(row.EmptyText, 72);
            elements.Empty.tooltip = row.EmptyTooltip;
            elements.Empty.style.display = string.IsNullOrWhiteSpace(row.EmptyText)
                ? DisplayStyle.None
                : DisplayStyle.Flex;
        }
        else
        {
            elements.Empty.text = string.Empty;
            elements.Empty.tooltip = string.Empty;
            elements.Empty.style.display = DisplayStyle.None;
        }
        ClearDynamic(elements);

        foreach (var card in row.Board.Cards)
        {
            var socket = card.DisplaySocketId ?? card.SourceSocketId;
            if (!socket.HasValue)
                continue;

            if (row.CanToggleCandidates)
                AddHitTarget(elements, row.Board.Id, card, socket.Value);

            if (candidates.Contains(card.TemplateId))
                AddCandidateMarker(elements, card, socket.Value);
        }
    }

    private void AddHitTarget(
        RowElements elements,
        BppItemBoardId rowId,
        BppItemBoardCard card,
        EContainerSocketId socket
    )
    {
        var hit = new VisualElement();
        var rect = ItemBoardSlotGridGeometry.ResolveOccupiedRect(
            100f,
            100f,
            (int)socket,
            card.DisplaySpan,
            0f,
            0f
        );
        hit.style.position = Position.Absolute;
        hit.style.left = Length.Percent(rect.X);
        hit.style.top = 0f;
        hit.style.bottom = 0f;
        hit.style.width = Length.Percent(rect.Width);
        hit.style.backgroundColor = Color.clear;
        hit.RegisterCallback<MouseDownEvent>(evt =>
        {
            if (evt.button != 0)
                return;

            CandidateToggleRequested?.Invoke(rowId, card.TemplateId);
            evt.StopPropagation();
        });
        elements.SlotHost.Add(hit);
        elements.HitTargets.Add(hit);
    }

    private void AddCandidateMarker(
        RowElements elements,
        BppItemBoardCard card,
        EContainerSocketId socket
    )
    {
        if (_foregroundRoot == null)
            return;

        var hostBounds = elements.SlotHost.worldBound;
        if (hostBounds.width <= 0f || hostBounds.height <= 0f)
            return;

        var rect = ItemBoardSlotGridGeometry.ResolveOccupiedRect(
            hostBounds.width,
            hostBounds.height,
            (int)socket,
            card.DisplaySpan,
            0f,
            4f
        );
        if (rect.Width <= 0f)
            return;

        var marker = new VisualElement();
        marker.pickingMode = PickingMode.Ignore;
        marker.style.position = Position.Absolute;
        marker.style.left = hostBounds.x + rect.X;
        marker.style.top = hostBounds.y + rect.Y;
        marker.style.width = rect.Width;
        marker.style.height = rect.Height;
        marker.style.backgroundColor = new Color(1f, 0.72f, 0.18f, 0.13f);
        marker.style.borderBottomColor = Colors.HistoryGoldAccent;
        marker.style.borderTopColor = Colors.HistoryGoldAccent;
        marker.style.borderLeftColor = Colors.HistoryGoldAccent;
        marker.style.borderRightColor = Colors.HistoryGoldAccent;
        marker.style.borderBottomWidth = 3f;
        marker.style.borderTopWidth = 3f;
        marker.style.borderLeftWidth = 3f;
        marker.style.borderRightWidth = 3f;

        var badge = CreateLabel(16, FontStyle.Bold, Color.black);
        badge.text = "✓";
        badge.pickingMode = PickingMode.Ignore;
        badge.style.position = Position.Absolute;
        badge.style.right = 6f;
        badge.style.top = 6f;
        badge.style.width = 26f;
        badge.style.height = 26f;
        badge.style.unityTextAlign = TextAnchor.MiddleCenter;
        badge.style.backgroundColor = Colors.HistoryGoldAccent;
        marker.Add(badge);

        _foregroundRoot.Add(marker);
        elements.Markers.Add(marker);
    }

    private static void ClearDynamic(RowElements elements)
    {
        foreach (var hit in elements.HitTargets)
            hit.RemoveFromHierarchy();
        foreach (var marker in elements.Markers)
            marker.RemoveFromHierarchy();
        elements.HitTargets.Clear();
        elements.Markers.Clear();
    }

    private void PublishRowBounds(BppItemBoardId id, VisualElement slotHost)
    {
        var worldBound = slotHost.worldBound;
        var ppp = slotHost.scaledPixelsPerPoint;
        var bounds = new Rect(
            Mathf.Round(worldBound.x * ppp),
            Mathf.Round(Screen.height - worldBound.yMax * ppp),
            Mathf.Max(1f, Mathf.Round(worldBound.width * ppp)),
            Mathf.Max(1f, Mathf.Round(worldBound.height * ppp))
        );
        RowBoundsChanged?.Invoke(id, bounds);
    }

    private static PanelSettings CreatePanelSettings(int sortingOrder)
    {
        var settings = ScriptableObject.CreateInstance<PanelSettings>();
        settings.sortingOrder = sortingOrder;
        settings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
        settings.referenceResolution = new Vector2Int(1920, 1080);
        settings.match = 1f;
        settings.clearColor = false;
        settings.targetDisplay = 0;
        return settings;
    }

    private static void ConfigureDocumentRoot(VisualElement root, PickingMode pickingMode)
    {
        root.style.flexGrow = 1f;
        root.style.position = Position.Absolute;
        root.style.left = 0f;
        root.style.right = 0f;
        root.style.top = 0f;
        root.style.bottom = 0f;
        root.style.display = DisplayStyle.None;
        root.pickingMode = pickingMode;
    }

    private static Color ResolveRefreshStatusColor(LiveBuildRefreshSeverity severity)
    {
        return severity switch
        {
            LiveBuildRefreshSeverity.Success => Colors.StatusCompletedText,
            LiveBuildRefreshSeverity.Failure => Colors.StatusAbandonedText,
            LiveBuildRefreshSeverity.Pending => Colors.StatusDefaultText,
            _ => Colors.HistoryStatusText,
        };
    }

    private static Label CreateLabel(int fontSize, FontStyle fontStyle, Color color)
    {
        var label = new Label();
        label.style.fontSize = fontSize;
        label.style.unityFont = BppUiFont.Default;
        label.style.unityFontStyleAndWeight = fontStyle;
        label.style.color = color;
        label.style.unityTextAlign = TextAnchor.MiddleLeft;
        return label;
    }

    private static Button CreateButton(string text, Action onClick)
    {
        var button = new Button(() => onClick()) { text = text };
        button.style.height = 40f;
        button.style.minWidth = 0f;
        button.style.flexShrink = 1f;
        button.style.unityFont = BppUiFont.Default;
        button.style.unityTextAlign = TextAnchor.MiddleCenter;
        button.style.justifyContent = Justify.Center;
        button.style.alignItems = Align.Center;
        button.style.overflow = Overflow.Hidden;
        button.style.backgroundColor = Colors.HistoryButtonBackground;
        button.style.color = Colors.White;
        button.style.borderBottomColor = Colors.HistoryButtonBorder;
        button.style.borderTopColor = Colors.HistoryButtonBorder;
        button.style.borderLeftColor = Colors.HistoryButtonBorder;
        button.style.borderRightColor = Colors.HistoryButtonBorder;
        button.style.borderBottomWidth = 1f;
        button.style.borderTopWidth = 1f;
        button.style.borderLeftWidth = 1f;
        button.style.borderRightWidth = 1f;
        var textElement = button.Q<TextElement>();
        if (textElement != null)
        {
            textElement.style.unityTextAlign = TextAnchor.MiddleCenter;
            textElement.style.flexGrow = 1f;
            textElement.style.flexShrink = 1f;
            textElement.style.minWidth = 0f;
            textElement.style.whiteSpace = WhiteSpace.NoWrap;
            textElement.style.overflow = Overflow.Hidden;
            textElement.style.unityFont = BppUiFont.Default;
        }
        button.tooltip = text;
        return button;
    }
}
