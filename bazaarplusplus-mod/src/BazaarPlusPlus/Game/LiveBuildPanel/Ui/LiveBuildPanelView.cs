#nullable enable

using System.Globalization;
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Game.LiveBuildPanel.Data;
using BazaarPlusPlus.Game.Supporters.Ui;
using BazaarPlusPlus.GameInterop.Fonts;
using BazaarPlusPlus.GameInterop.Heroes;
using BazaarPlusPlus.GameInterop.ItemBoardPreview;
using BazaarPlusPlus.Infrastructure;
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
    private NativeGameTypography.PanelScope? _typography;
    private NativeGameTypography.PanelScope? _foregroundTypography;
    private NativeGameTitleOverlay? _titleOverlay;
    private VisualElement? _foregroundRoot;
    private VisualElement? _root;
    private Label? _title;
    private VisualElement? _subtitle;
    private Label? _corpusCardTitle;
    private Button? _finalBuildRefreshButton;
    private Label? _corpusStatus;
    private VisualElement? _corpusDashboard;
    private Label? _corpusFreshness;
    private VisualElement? _heroStrip;
    private Label? _resultCardTitle;
    private Label? _matchesPager;
    private VisualElement? _matchesStats;
    private Label? _matchesGuidance;
    private Label? _rateValue;
    private Label? _sampleValue;
    private Label? _finalDayValue;
    private Label? _matchedValue;
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

        _panelSettings = CreatePanelSettings(BppOverlaySorting.PanelUiToolkit);
        if (
            NativeGameTypography.TryAttachPanel(_panelSettings, out _typography)
                != NativeGameTypography.Outcome.Ready
            || _typography == null
        )
        {
            AbandonPanelSettingsCreation();
            return;
        }

        _foregroundPanelSettings = CreatePanelSettings(BppOverlaySorting.PanelForeground);
        if (
            NativeGameTypography.TryAttachPanel(_foregroundPanelSettings, out _foregroundTypography)
                != NativeGameTypography.Outcome.Ready
            || _foregroundTypography == null
        )
        {
            AbandonPanelSettingsCreation();
            return;
        }
        if (
            !NativeGameTitleOverlay.TryCreate(
                "LiveBuildPanelNativeTitle",
                _parent,
                BppOverlaySorting.NativeCardPreview,
                Sizes.FontTitle,
                Colors.GameTitleText,
                out _titleOverlay
            )
            || _titleOverlay == null
        )
        {
            AbandonPanelSettingsCreation();
            return;
        }

        _rootObject = new GameObject("LiveBuildPanelUiToolkitRoot");
        _rootObject.transform.SetParent(_parent, false);
        _document = _rootObject.AddComponent<UIDocument>();
        _document.panelSettings = _panelSettings;
        _root = _document.rootVisualElement;
        ConfigureDocumentRoot(_root, PickingMode.Position);
        _typography.Apply(_root);

        _foregroundRootObject = new GameObject("LiveBuildPanelForegroundUiToolkitRoot");
        _foregroundRootObject.transform.SetParent(_parent, false);
        _foregroundDocument = _foregroundRootObject.AddComponent<UIDocument>();
        _foregroundDocument.panelSettings = _foregroundPanelSettings;
        _foregroundRoot = _foregroundDocument.rootVisualElement;
        ConfigureDocumentRoot(_foregroundRoot, PickingMode.Ignore);
        _foregroundTypography.Apply(_foregroundRoot);

        BuildTree(_root);
        _titleOverlay.Attach(_title!);
    }

    public void SetVisible(bool visible)
    {
        if (_root != null)
            _root.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        if (_foregroundRoot != null)
            _foregroundRoot.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        _titleOverlay?.SetVisible(visible);
    }

    public void Refresh(LiveBuildPanelSnapshot snapshot)
    {
        if (_root == null)
            return;

        _title!.text = LiveBuildPanelText.Title();
        _titleOverlay?.SetText(_title.text);
        _closeButton!.text = LiveBuildPanelText.Close();
        BPPSupporterAttributionRow.Bind(
            _subtitle!,
            snapshot.Supporters,
            LiveBuildPanelText.Subtitle(),
            _typography!
        );
        _corpusCardTitle!.text = LiveBuildPanelText.CorpusCardTitle();
        _finalBuildRefreshButton!.text = snapshot.FinalBuildRefreshButtonText;
        _finalBuildRefreshButton.tooltip = snapshot.FinalBuildRefreshButtonText;
        _finalBuildRefreshButton.SetEnabled(snapshot.FinalBuildRefreshButtonEnabled);
        RefreshCorpusCard(snapshot);
        _resultCardTitle!.text = LiveBuildPanelText.ResultCardTitle();
        RefreshMatchesCard(snapshot);
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

    // The corpus card swaps between the per-hero dashboard (summary) and a single status line
    // (pending/failure/empty) by toggling display only — the card is fixed-height, so no reflow.
    private void RefreshCorpusCard(LiveBuildPanelSnapshot snapshot)
    {
        var isSummary = snapshot.CorpusState == LiveBuildCorpusState.Summary;
        _corpusDashboard!.style.display = isSummary ? DisplayStyle.Flex : DisplayStyle.None;
        _corpusStatus!.style.display = isSummary ? DisplayStyle.None : DisplayStyle.Flex;

        if (isSummary)
        {
            _corpusFreshness!.text = snapshot.CorpusFreshnessText;
            _corpusFreshness.tooltip = snapshot.CorpusFreshnessTooltip;
            _corpusFreshness.style.color = ResolveRefreshStatusColor(
                snapshot.CorpusFreshnessSeverity
            );
            RebuildHeroStrip(snapshot.CorpusSummary);
            return;
        }

        _corpusStatus.text = StablePanelText.Compact(snapshot.CorpusStatusText, 96);
        _corpusStatus.tooltip = string.IsNullOrWhiteSpace(snapshot.CorpusStatusTooltip)
            ? snapshot.CorpusStatusText
            : snapshot.CorpusStatusTooltip;
        _corpusStatus.style.color = ResolveRefreshStatusColor(snapshot.CorpusStatusSeverity);
    }

    private void RebuildHeroStrip(TenWinCorpusSummary? summary)
    {
        _heroStrip!.Clear();
        if (summary is not { } value)
            return;

        foreach (var entry in value.HeroBuildCounts)
        {
            if (!HeroVisual.IsPlayableHero(entry.Hero))
                continue;

            _heroStrip.Add(BuildHeroTile(entry));
        }
    }

    private static VisualElement BuildHeroTile(TenWinHeroBuildCount entry)
    {
        var badge = HeroVisual.Resolve(entry.Hero);
        var count = entry.BuildCount.ToString("N0", CultureInfo.CurrentCulture);

        var tile = new VisualElement();
        tile.style.flexGrow = 1f;
        tile.style.flexBasis = 0f;
        tile.style.minWidth = 0f;
        tile.style.alignItems = Align.Center;
        tile.style.overflow = Overflow.Hidden;
        tile.tooltip = $"{entry.Hero} {count}";

        var countLabel = CreateLabel(14, FontStyle.Bold, Colors.HistoryProgressText);
        countLabel.text = count;
        countLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
        countLabel.style.whiteSpace = WhiteSpace.NoWrap;
        tile.Add(countLabel);

        var chip = CreateLabel(11, FontStyle.Bold, badge.Text);
        chip.text = badge.ShortCode;
        chip.style.marginTop = 2f;
        chip.style.height = 18f;
        chip.style.backgroundColor = badge.Background;
        chip.style.unityTextAlign = TextAnchor.MiddleCenter;
        chip.style.whiteSpace = WhiteSpace.NoWrap;
        UiStyle.HorizontalPadding(chip.style, 3f);
        UiStyle.Radius(chip.style, Radii.InfoChip);
        tile.Add(chip);

        return tile;
    }

    // The Matches card swaps between the per-recommendation stat rows (has recommendation) and a
    // single guidance line (no run / no candidates / no matching build) by toggling display only.
    private void RefreshMatchesCard(LiveBuildPanelSnapshot snapshot)
    {
        var hasRecommendation = snapshot.MatchesState == LiveBuildMatchesState.HasRecommendation;
        _matchesStats!.style.display = hasRecommendation ? DisplayStyle.Flex : DisplayStyle.None;
        _matchesPager!.style.display = hasRecommendation ? DisplayStyle.Flex : DisplayStyle.None;
        _matchesGuidance!.style.display = hasRecommendation ? DisplayStyle.None : DisplayStyle.Flex;

        if (hasRecommendation)
        {
            _matchesPager.text = LiveBuildPanelText.RecommendationCount(
                snapshot.RecommendationIndex,
                snapshot.RecommendationCount
            );
            _rateValue!.text = LiveBuildPanelText.MatchRateValue(snapshot.MatchTenWinRateBps);
            _sampleValue!.text = LiveBuildPanelText.MatchSampleValue(snapshot.MatchTenWinRunCount);
            _finalDayValue!.text = LiveBuildPanelText.MatchFinalDayValue(snapshot.MatchP75FinalDay);
            _matchedValue!.text = LiveBuildPanelText.MatchMatchedValue(
                snapshot.MatchMatchedCardCount,
                snapshot.CandidateTemplateIds.Count
            );
            return;
        }

        _matchesGuidance.text = StablePanelText.Compact(snapshot.MatchesGuidance, 96);
        _matchesGuidance.tooltip = snapshot.MatchesGuidance;
    }

    private static Label AddStatRow(VisualElement parent, string label)
    {
        var row = new VisualElement();
        row.style.flexDirection = FlexDirection.Row;
        row.style.alignItems = Align.Center;
        row.style.marginBottom = 4f;

        var labelElement = CreateLabel(13, FontStyle.Normal, Colors.HistoryFooterSecondaryText);
        labelElement.text = label;
        labelElement.style.flexGrow = 1f;
        labelElement.style.flexShrink = 1f;
        labelElement.style.minWidth = 0f;
        labelElement.style.whiteSpace = WhiteSpace.NoWrap;
        labelElement.style.overflow = Overflow.Hidden;
        row.Add(labelElement);

        var value = CreateLabel(13, FontStyle.Bold, Colors.HistoryProgressText);
        value.style.flexShrink = 0f;
        value.style.marginLeft = 8f;
        value.style.unityTextAlign = TextAnchor.MiddleRight;
        value.style.whiteSpace = WhiteSpace.NoWrap;
        row.Add(value);

        parent.Add(row);
        return value;
    }

    public void Dispose()
    {
        if (_rootObject != null)
            UnityEngine.Object.Destroy(_rootObject);
        _titleOverlay?.Dispose();
        _typography?.Dispose();
        if (_panelSettings != null)
            UnityEngine.Object.Destroy(_panelSettings);
        if (_foregroundRootObject != null)
            UnityEngine.Object.Destroy(_foregroundRootObject);
        _foregroundTypography?.Dispose();
        if (_foregroundPanelSettings != null)
            UnityEngine.Object.Destroy(_foregroundPanelSettings);

        _rows.Clear();
        _rootObject = null;
        _document = null;
        _panelSettings = null;
        _foregroundRootObject = null;
        _foregroundDocument = null;
        _foregroundPanelSettings = null;
        _typography = null;
        _foregroundTypography = null;
        _titleOverlay = null;
        _foregroundRoot = null;
        _root = null;
    }

    private void AbandonPanelSettingsCreation()
    {
        _typography?.Dispose();
        _foregroundTypography?.Dispose();
        _titleOverlay?.Dispose();
        if (_panelSettings != null)
            UnityEngine.Object.DestroyImmediate(_panelSettings);
        if (_foregroundPanelSettings != null)
            UnityEngine.Object.DestroyImmediate(_foregroundPanelSettings);
        _panelSettings = null;
        _foregroundPanelSettings = null;
        _typography = null;
        _foregroundTypography = null;
        _titleOverlay = null;
    }

    private void BuildTree(VisualElement root)
    {
        var panel = new VisualElement();
        panel.style.flexGrow = 1f;
        panel.style.minHeight = 0f;
        panel.style.flexDirection = FlexDirection.Row;
        panel.style.backgroundColor = Colors.HistoryPanelBackground;
        panel.style.paddingLeft = UiSpacing.PanelPadding;
        panel.style.paddingRight = UiSpacing.PanelPadding;
        panel.style.paddingTop = UiSpacing.PanelPadding;
        panel.style.paddingBottom = UiSpacing.PanelPadding;
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
        UiStyle.Border(row.style, Borders.Thin, Colors.HistoryListFrameBorder);
        UiStyle.Radius(row.style, Radii.Row);
        row.style.flexDirection = FlexDirection.Row;
        row.style.overflow = Overflow.Hidden;
        parent.Add(row);

        var labelColumn = new VisualElement();
        labelColumn.style.width = 144f;
        labelColumn.style.flexShrink = 0f;
        labelColumn.style.paddingLeft = 14f;
        labelColumn.style.paddingRight = 12f;
        labelColumn.style.justifyContent = Justify.Center;
        labelColumn.style.overflow = Overflow.Hidden;
        row.Add(labelColumn);

        var title = CreateLabel(16, FontStyle.Bold, Colors.HistorySectionTitleText);
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
        // Match the house info-rail sizing (HistoryPanel / CollectionPanel use the OperationRail
        // tokens) so the right column lines up across panels; LiveBuild runs a slightly narrower
        // 25% basis to suit its more compact content.
        var rail = new VisualElement();
        rail.style.flexGrow = 0f;
        rail.style.flexShrink = 0f;
        rail.style.flexBasis = Length.Percent(Sizes.LiveBuildRailWidthPercent);
        rail.style.minWidth = Sizes.OperationRailMinWidth;
        rail.style.maxWidth = Sizes.OperationRailMaxWidth;
        rail.style.marginLeft = UiSpacing.ColumnGap;
        rail.style.minHeight = 0f;
        rail.style.overflow = Overflow.Hidden;
        rail.style.flexDirection = FlexDirection.Column;
        parent.Add(rail);

        var titleRow = new VisualElement();
        titleRow.style.flexDirection = FlexDirection.Row;
        titleRow.style.alignItems = Align.Center;
        rail.Add(titleRow);

        _title = CreateLabel(Sizes.FontTitle, FontStyle.Normal, Colors.GameTitleText);
        _title.style.flexGrow = 1f;
        _title.style.flexShrink = 1f;
        _title.style.minWidth = 0f;
        _title.style.whiteSpace = WhiteSpace.NoWrap;
        _title.style.overflow = Overflow.Hidden;
        titleRow.Add(_title);

        _closeButton = CreateButton(LiveBuildPanelText.Close(), _close);
        _closeButton.style.width = 86f;
        StyleButton(_closeButton, Colors.CloseBackground, Colors.CloseText);
        titleRow.Add(_closeButton);

        _subtitle = BPPSupporterAttributionRow.Create();
        _subtitle.style.marginTop = 10f;
        rail.Add(_subtitle);

        // Corpus card: ten-win coverage (freshness + per-hero tiles) with the pull action in its
        // header, next to the data it refreshes. Fixed height on purpose — the body swaps the
        // dashboard vs a single status line (pending/failure/empty) in place, so the rail never reflows.
        var corpusCard = new VisualElement();
        corpusCard.style.marginTop = 14f;
        corpusCard.style.height = Sizes.LiveBuildCorpusCardHeight;
        corpusCard.style.minHeight = Sizes.LiveBuildCorpusCardHeight;
        corpusCard.style.maxHeight = Sizes.LiveBuildCorpusCardHeight;
        corpusCard.style.backgroundColor = Colors.HistoryStatusBackground;
        corpusCard.style.paddingLeft = 12f;
        corpusCard.style.paddingRight = 10f;
        corpusCard.style.paddingTop = 10f;
        corpusCard.style.paddingBottom = 10f;
        corpusCard.style.overflow = Overflow.Hidden;
        UiStyle.Border(corpusCard.style, Borders.Thin, Colors.HistoryStatusBorder);
        UiStyle.Radius(corpusCard.style, Radii.Md);
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
        UiStyle.FixedWidth(_finalBuildRefreshButton.style, Sizes.LiveBuildRefreshButtonWidth);
        UiStyle.FixedHeight(_finalBuildRefreshButton.style, Sizes.LiveBuildRefreshButtonHeight);
        _finalBuildRefreshButton.style.flexGrow = 0f;
        _finalBuildRefreshButton.style.flexShrink = 0f;
        corpusHeader.Add(_finalBuildRefreshButton);

        _corpusDashboard = new VisualElement();
        _corpusDashboard.style.marginTop = 8f;
        _corpusDashboard.style.flexDirection = FlexDirection.Column;
        _corpusDashboard.style.overflow = Overflow.Hidden;
        corpusCard.Add(_corpusDashboard);

        _corpusFreshness = CreateLabel(12, FontStyle.Normal, Colors.HistoryFooterSecondaryText);
        _corpusFreshness.style.whiteSpace = WhiteSpace.NoWrap;
        _corpusFreshness.style.overflow = Overflow.Hidden;
        _corpusDashboard.Add(_corpusFreshness);

        _heroStrip = new VisualElement();
        _heroStrip.style.flexDirection = FlexDirection.Row;
        _heroStrip.style.marginTop = 8f;
        _heroStrip.style.overflow = Overflow.Hidden;
        _corpusDashboard.Add(_heroStrip);

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
        UiStyle.Border(resultCard.style, Borders.Thin, Colors.HistoryStatusBorder);
        UiStyle.Radius(resultCard.style, Radii.Md);
        rail.Add(resultCard);

        var resultHeader = new VisualElement();
        resultHeader.style.flexDirection = FlexDirection.Row;
        resultHeader.style.alignItems = Align.Center;
        resultCard.Add(resultHeader);

        _resultCardTitle = CreateLabel(15, FontStyle.Bold, Colors.HistorySectionTitleText);
        _resultCardTitle.style.flexGrow = 1f;
        _resultCardTitle.style.flexShrink = 1f;
        _resultCardTitle.style.minWidth = 0f;
        _resultCardTitle.style.whiteSpace = WhiteSpace.NoWrap;
        _resultCardTitle.style.overflow = Overflow.Hidden;
        resultHeader.Add(_resultCardTitle);

        _matchesPager = CreateLabel(12, FontStyle.Bold, Colors.HistoryChipText);
        _matchesPager.style.flexShrink = 0f;
        _matchesPager.style.height = Sizes.InfoChipHeight;
        _matchesPager.style.unityTextAlign = TextAnchor.MiddleCenter;
        _matchesPager.style.backgroundColor = Colors.HistoryChipBackground;
        UiStyle.HorizontalPadding(_matchesPager.style, 8f);
        UiStyle.Radius(_matchesPager.style, Radii.InfoChip);
        resultHeader.Add(_matchesPager);

        _matchesStats = new VisualElement();
        _matchesStats.style.marginTop = 8f;
        _matchesStats.style.flexDirection = FlexDirection.Column;
        _matchesStats.style.overflow = Overflow.Hidden;
        resultCard.Add(_matchesStats);

        _rateValue = AddStatRow(_matchesStats, LiveBuildPanelText.MatchRateLabel());
        _rateValue.style.color = Colors.StatusCompletedText;
        _sampleValue = AddStatRow(_matchesStats, LiveBuildPanelText.MatchSampleLabel());
        _finalDayValue = AddStatRow(_matchesStats, LiveBuildPanelText.MatchFinalDayLabel());
        _matchedValue = AddStatRow(_matchesStats, LiveBuildPanelText.MatchMatchedLabel());

        _matchesGuidance = CreateLabel(14, FontStyle.Normal, Colors.HistoryStatusText);
        _matchesGuidance.style.marginTop = 8f;
        _matchesGuidance.style.whiteSpace = WhiteSpace.Normal;
        _matchesGuidance.style.maxHeight = Sizes.LiveBuildRecommendationStatusMaxHeight;
        _matchesGuidance.style.overflow = Overflow.Hidden;
        resultCard.Add(_matchesGuidance);

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
        button.style.unityTextAlign = TextAnchor.MiddleCenter;
        button.style.justifyContent = Justify.Center;
        button.style.alignItems = Align.Center;
        button.style.overflow = Overflow.Hidden;
        button.style.backgroundColor = Colors.HistoryButtonBackground;
        button.style.color = Colors.White;
        UiStyle.Border(button.style, Borders.Thin, Colors.HistoryButtonBorder);
        UiStyle.Radius(button.style, Radii.Md);
        var textElement = button.Q<TextElement>();
        if (textElement != null)
        {
            textElement.style.unityTextAlign = TextAnchor.MiddleCenter;
            textElement.style.flexGrow = 1f;
            textElement.style.flexShrink = 1f;
            textElement.style.minWidth = 0f;
            textElement.style.whiteSpace = WhiteSpace.NoWrap;
            textElement.style.overflow = Overflow.Hidden;
        }
        button.tooltip = text;
        return button;
    }

    private static void StyleButton(Button button, Color background, Color textColor)
    {
        button.style.backgroundColor = background;
        button.style.color = textColor;
        UiStyle.BorderColor(button.style, Colors.ButtonBorderFor(background));
    }
}
