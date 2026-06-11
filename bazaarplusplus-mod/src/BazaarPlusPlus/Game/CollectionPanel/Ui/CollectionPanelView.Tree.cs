#nullable enable
using System;
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Game.CollectionPanel.Data;
using BazaarPlusPlus.Game.CollectionPanel.Grid;
using BazaarPlusPlus.Game.Supporters.Ui;
using BazaarPlusPlus.Infrastructure.UiTokens;
using UnityEngine;
using UnityEngine.UIElements;

namespace BazaarPlusPlus.Game.CollectionPanel.Ui;

internal sealed partial class CollectionPanelView
{
    private void BuildTree(VisualElement root)
    {
        var panel = new VisualElement();
        panel.style.flexGrow = 1f;
        panel.style.backgroundColor = Colors.HistoryPanelBackground;
        panel.style.paddingLeft = UiSpacing.PanelPadding;
        panel.style.paddingRight = UiSpacing.PanelPadding;
        panel.style.paddingTop = UiSpacing.PanelPadding;
        panel.style.paddingBottom = UiSpacing.Xxl;
        panel.style.flexDirection = FlexDirection.Row;
        root.Add(panel);

        BuildGrid(panel);
        BuildOperationRail(panel);
    }

    private void BuildOperationRail(VisualElement parent)
    {
        var rail = new VisualElement();
        rail.style.flexDirection = FlexDirection.Column;
        rail.style.flexGrow = 0f;
        rail.style.flexShrink = 0f;
        rail.style.flexBasis = Length.Percent(Sizes.OperationRailWidthPercent);
        rail.style.minWidth = Sizes.OperationRailMinWidth;
        rail.style.maxWidth = Sizes.OperationRailMaxWidth;
        rail.style.minHeight = 0f;
        rail.style.overflow = Overflow.Hidden;
        rail.style.marginLeft = UiSpacing.ColumnGap;
        parent.Add(rail);

        // Title + count + Close (Close lives here in the operation area, not a top bar).
        var titleRow = new VisualElement();
        titleRow.style.flexDirection = FlexDirection.Row;
        titleRow.style.alignItems = Align.Center;
        rail.Add(titleRow);

        _title = CreateLabel(Sizes.FontTitle, FontStyle.Bold, Colors.HistoryTitleText);
        _title.style.flexGrow = 1f;
        _title.style.flexShrink = 1f;
        _title.style.minWidth = 0f;
        _title.style.whiteSpace = WhiteSpace.NoWrap;
        _title.style.overflow = Overflow.Hidden;
        titleRow.Add(_title);

        _countLabel = CreateCountLabel();
        _countLabel.style.marginRight = UiSpacing.Sm;
        titleRow.Add(_countLabel);

        _closeButton = CreateButton(
            CollectionPanelText.Close(),
            _commands.Close,
            Sizes.CloseButtonWidth,
            Sizes.ButtonStandardHeight
        );
        StyleButton(_closeButton, Colors.CloseBackground, Colors.CloseText);
        titleRow.Add(_closeButton);

        _subtitle = BPPSupporterAttributionRow.Create();
        rail.Add(_subtitle);

        var primaryControlsRow = CreateOperationRow(UiSpacing.Sm);
        rail.Add(primaryControlsRow);

        _itemTabButton = CreateButton(
            CollectionPanelText.ItemsTab(),
            () => _commands.SetActiveType(ECardType.Item),
            Sizes.RunsTabWidth,
            Sizes.ButtonStandardHeight
        );
        _skillTabButton = CreateButton(
            CollectionPanelText.SkillsTab(),
            () => _commands.SetActiveType(ECardType.Skill),
            Sizes.RunsTabWidth,
            Sizes.ButtonStandardHeight
        );
        primaryControlsRow.Add(_itemTabButton);
        _packageToggleButton = CreatePackageTabButton();
        _packageToggleButton.style.marginLeft = UiSpacing.Md;
        primaryControlsRow.Add(_packageToggleButton);
        _skillTabButton.style.marginLeft = UiSpacing.Md;
        primaryControlsRow.Add(_skillTabButton);

        primaryControlsRow.Add(CreateOperationSpacer());

        var sortGroup = new VisualElement();
        sortGroup.style.flexDirection = FlexDirection.Row;
        sortGroup.style.alignItems = Align.Center;
        sortGroup.style.flexShrink = 0f;
        sortGroup.style.marginTop = UiSpacing.Xs;
        primaryControlsRow.Add(sortGroup);

        _sortLabel = CreateLabel(Sizes.FontSmall, FontStyle.Bold, Colors.HistorySubtitleText);
        _sortLabel.text = CollectionPanelText.SortHeader();
        _sortLabel.style.marginRight = UiSpacing.Xs;
        sortGroup.Add(_sortLabel);

        _sortQualityButton = CreateInlineSortButton(
            CollectionPanelText.SortQuality(),
            () => _commands.SetSortPriority(CollectionSortPriority.Quality)
        );
        _sortSizeButton = CreateInlineSortButton(
            CollectionPanelText.SortSize(),
            () => _commands.SetSortPriority(CollectionSortPriority.Size)
        );
        sortGroup.Add(_sortQualityButton);
        _sortSizeButton.style.marginLeft = UiSpacing.Xs;
        sortGroup.Add(_sortSizeButton);

        // Compact day-number icon toggle.
        _dayToggleButton = CreateDayToggleButton();
        _dayToggleButton.style.marginLeft = UiSpacing.Sm;
        _dayToggleButton.style.marginTop = UiSpacing.Xs;
        primaryControlsRow.Add(_dayToggleButton);

        var controlsScroll = new ScrollView(ScrollViewMode.Vertical);
        controlsScroll.style.flexGrow = 1f;
        controlsScroll.style.flexShrink = 1f;
        controlsScroll.style.minHeight = 0f;
        controlsScroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
        controlsScroll.verticalScrollerVisibility = ScrollerVisibility.Auto;
        controlsScroll.mouseWheelScrollSize = CollectionGridConstants.MouseWheelScrollPoints;
        controlsScroll.contentContainer.style.flexDirection = FlexDirection.Column;
        controlsScroll.contentContainer.style.minHeight = 0f;
        _controlsScrollView = controlsScroll;
        rail.Add(controlsScroll);

        // Hero filter.
        CreateFilterSection(
            controlsScroll,
            CollectionPanelText.HeroHeader(),
            UiSpacing.Xl,
            out _heroChipRow,
            out _heroFilterLabel
        );
        _heroChipRow.style.flexWrap = Wrap.NoWrap;
        _heroChipRow.style.justifyContent = Justify.FlexStart;
        _heroChipRow.RegisterCallback<GeometryChangedEvent>(OnHeroChipRowGeometryChanged);

        // Size + tier filter. On Skills, Refresh hides Size and lets Quality fill the row.
        CreateFilterSection(
            controlsScroll,
            CollectionPanelText.TierSizeHeader(),
            UiSpacing.Lg,
            out var tierSizeChipRow,
            out _tierFilterLabel
        );
        tierSizeChipRow.style.flexWrap = Wrap.NoWrap;
        tierSizeChipRow.style.justifyContent = Justify.FlexStart;
        _sizeChipRow = CreateCombinedFilterChipSegment(3f);
        _tierChipRow = CreateCombinedFilterChipSegment(5f);
        _tierSizeDivider = CreateCombinedFilterDivider();
        tierSizeChipRow.Add(_sizeChipRow);
        tierSizeChipRow.Add(_tierSizeDivider);
        tierSizeChipRow.Add(_tierChipRow);
        _tierChipRow.style.flexWrap = Wrap.NoWrap;
        _tierChipRow.style.justifyContent = Justify.FlexStart;

        // Keyword filter (EHiddenTag gameplay keywords). This is the common secondary filter for
        // Items, Packages, and Skills, so keep it directly below Quality.
        _keywordFilterSection = CreateFilterSection(
            controlsScroll,
            CollectionPanelText.KeywordHeader(),
            UiSpacing.Lg,
            out _keywordChipRow,
            out _keywordFilterLabel,
            out var keywordHeaderRow
        );
        _keywordMatchModeButton = CreateFacetMatchModeButton(_commands.ToggleKeywordMatchMode);
        keywordHeaderRow.Add(_keywordMatchModeButton);
        _keywordChipRow.style.flexWrap = Wrap.Wrap;
        _keywordChipRow.style.justifyContent = Justify.FlexStart;

        // Tag filter (player-facing item categories). Items/Packages show this below gameplay
        // keywords; Skills hide it and source filters move up naturally.
        _tagFilterSection = CreateFilterSection(
            controlsScroll,
            CollectionPanelText.TagHeader(),
            UiSpacing.Lg,
            out _tagChipRow,
            out _tagFilterLabel,
            out var tagHeaderRow
        );
        _tagMatchModeButton = CreateFacetMatchModeButton(_commands.ToggleTagMatchMode);
        tagHeaderRow.Add(_tagMatchModeButton);
        _tagChipRow.style.flexWrap = Wrap.Wrap;
        _tagChipRow.style.justifyContent = Justify.FlexStart;

        // Source filter (merchant portraits on Items, trainer portraits on Skills).
        _sourceFilterSection = CreateFilterSection(
            controlsScroll,
            CollectionPanelText.SourceHeader(ECardType.Item),
            UiSpacing.Lg,
            out _sourceChipRow,
            out _sourceFilterLabel
        );
        _sourceChipRow.style.flexDirection = FlexDirection.Column;
        _sourceChipRow.style.flexWrap = Wrap.NoWrap;
        _sourceChipRow.style.justifyContent = Justify.FlexStart;
        _sourceChipRow.RegisterCallback<GeometryChangedEvent>(OnSourceChipRowGeometryChanged);

        _disclaimerLabel = CreateLabel(
            Sizes.FontCorner,
            FontStyle.Normal,
            Colors.HistoryFooterSecondaryText
        );
        _disclaimerLabel.text = CollectionPanelText.SourceDisclaimer();
        _disclaimerLabel.tooltip = _disclaimerLabel.text;
        _disclaimerLabel.style.marginTop = UiSpacing.Md;
        _disclaimerLabel.style.flexShrink = 0f;
        _disclaimerLabel.style.width = Length.Percent(100f);
        _disclaimerLabel.style.whiteSpace = WhiteSpace.Normal;
        _disclaimerLabel.style.maxHeight = Sizes.DetailTextMaxHeight;
        _disclaimerLabel.style.overflow = Overflow.Hidden;
        rail.Add(_disclaimerLabel);

        _statusLabel = CreateLabel(Sizes.FontSmall, FontStyle.Normal, Colors.HistoryStatusText);
        _statusLabel.style.marginTop = UiSpacing.Md;
        _statusLabel.style.flexShrink = 0f;
        _statusLabel.style.minHeight = Sizes.StatusHeight;
        _statusLabel.style.maxHeight = Sizes.CollectionStatusMaxHeight;
        _statusLabel.style.width = Length.Percent(100f);
        _statusLabel.style.whiteSpace = WhiteSpace.Normal;
        _statusLabel.style.overflow = Overflow.Hidden;
        _statusLabel.style.display = DisplayStyle.None;
        rail.Add(_statusLabel);
    }

    private static VisualElement CreateOperationRow(float marginTop)
    {
        var row = new VisualElement();
        row.style.flexDirection = FlexDirection.Row;
        row.style.alignItems = Align.Center;
        row.style.flexWrap = Wrap.Wrap;
        row.style.flexShrink = 0f;
        row.style.marginTop = marginTop;
        return row;
    }

    private static VisualElement CreateOperationSpacer()
    {
        var spacer = new VisualElement();
        spacer.style.flexGrow = 1f;
        spacer.style.flexShrink = 1f;
        spacer.style.minWidth = UiSpacing.Md;
        return spacer;
    }

    private static Label CreateCountLabel()
    {
        var label = CreateLabel(Sizes.FontSmall, FontStyle.Bold, Colors.HistoryStatusText);
        label.style.backgroundColor = Colors.HistoryStatusBackground;
        label.style.height = Sizes.ButtonCompactHeight;
        UiStyle.FixedWidth(label.style, Sizes.CollectionMatchCountWidth);
        label.style.flexShrink = 0f;
        label.style.whiteSpace = WhiteSpace.NoWrap;
        label.style.overflow = Overflow.Hidden;
        label.style.unityTextAlign = TextAnchor.MiddleCenter;
        label.style.alignSelf = Align.Center;
        UiStyle.HorizontalPadding(label.style, UiSpacing.Md);
        UiStyle.Radius(label.style, Radii.Md);
        UiStyle.Border(label.style, Borders.Thin, Colors.HistoryStatusBorder);
        return label;
    }

    private static Button CreateInlineSortButton(string text, Action onClick)
    {
        var button = CreateButton(text, onClick, Sizes.RunsTabWidth, Sizes.ButtonStandardHeight);
        button.style.flexShrink = 0f;
        StyleButton(button, Colors.HistoryChipBackground, Colors.HistoryChipText);
        return button;
    }

    private static VisualElement CreateFilterSection(
        VisualElement parent,
        string title,
        float marginTop,
        out VisualElement chipRow
    ) => CreateFilterSection(parent, title, marginTop, out chipRow, out _);

    private static VisualElement CreateFilterSection(
        VisualElement parent,
        string title,
        float marginTop,
        out VisualElement chipRow,
        out Label label
    ) => CreateFilterSection(parent, title, marginTop, out chipRow, out label, out _);

    private static VisualElement CreateFilterSection(
        VisualElement parent,
        string title,
        float marginTop,
        out VisualElement chipRow,
        out Label label,
        out VisualElement headerRow
    )
    {
        var section = new VisualElement();
        section.style.flexDirection = FlexDirection.Column;
        section.style.flexShrink = 0f;
        section.style.marginTop = marginTop;
        parent.Add(section);

        headerRow = new VisualElement();
        headerRow.style.flexDirection = FlexDirection.Row;
        headerRow.style.alignItems = Align.Center;
        headerRow.style.alignSelf = Align.Stretch;
        headerRow.style.marginBottom = UiSpacing.Sm;
        section.Add(headerRow);

        label = CreateLabel(Sizes.FontSmall, FontStyle.Bold, Colors.HistorySubtitleText);
        label.text = title;
        label.style.flexGrow = 1f;
        label.style.flexShrink = 1f;
        label.style.minWidth = 0f;
        label.style.whiteSpace = WhiteSpace.NoWrap;
        label.style.overflow = Overflow.Hidden;
        headerRow.Add(label);

        chipRow = new VisualElement();
        chipRow.style.flexDirection = FlexDirection.Row;
        chipRow.style.flexWrap = Wrap.Wrap;
        chipRow.style.alignItems = Align.Center;
        chipRow.style.alignSelf = Align.Stretch;
        section.Add(chipRow);

        return section;
    }

    private static Button CreateFacetMatchModeButton(Action onClick)
    {
        var button = CreateButton(
            string.Empty,
            onClick,
            Sizes.FacetModeToggleWidth,
            Sizes.InfoChipHeight
        );
        button.style.marginLeft = UiSpacing.Sm;
        button.style.fontSize = Sizes.FontSmall;
        StyleButton(button, Colors.HistoryChipBackground, Colors.HistoryChipText);
        return button;
    }

    private static VisualElement CreateCombinedFilterChipSegment(float flexGrow)
    {
        var segment = new VisualElement();
        segment.style.flexDirection = FlexDirection.Row;
        segment.style.flexWrap = Wrap.NoWrap;
        segment.style.alignItems = Align.Center;
        segment.style.flexGrow = flexGrow;
        segment.style.flexShrink = 1f;
        segment.style.minWidth = 0f;
        return segment;
    }

    private static VisualElement CreateCombinedFilterDivider()
    {
        var divider = new VisualElement { pickingMode = PickingMode.Ignore };
        divider.style.width = Borders.Thin;
        divider.style.height = Sizes.InfoChipHeight;
        divider.style.marginLeft = UiSpacing.Sm;
        divider.style.marginRight = UiSpacing.Sm;
        divider.style.flexShrink = 0f;
        divider.style.backgroundColor = Colors.HistoryButtonBorder;
        divider.style.opacity = 0.72f;
        return divider;
    }

    private Button CreatePackageTabButton()
    {
        var button = CreateButton(
            CollectionPanelText.PackagesToggle(),
            _commands.TogglePackagesOnly,
            Sizes.RunsTabWidth,
            Sizes.ButtonStandardHeight
        );
        StyleButton(button, Colors.RunsTabBackground, Colors.White);
        return button;
    }

    // Compact day-number "icon": shows the effective day (current run day, or OutOfRunDay) and
    // tapping toggles whether the day participates in filtering. RefreshDayToggle sets the number
    // and the active highlight; the tooltip names the control since the face is just a number.
    private Button CreateDayToggleButton()
    {
        var button = CreateButton(
            string.Empty,
            _commands.ToggleRunDayFilter,
            Sizes.DayIconWidth,
            Sizes.ButtonStandardHeight
        );
        button.tooltip = CollectionPanelText.DayHeader();
        StyleButton(button, Colors.HistoryChipBackground, Colors.HistoryChipText);
        return button;
    }

    private void BuildGrid(VisualElement parent)
    {
        _gridViewport = new VisualElement();
        _gridViewport.style.flexGrow = 1f;
        _gridViewport.style.flexShrink = 1f;
        _gridViewport.style.minHeight = 0f;
        _gridViewport.style.minWidth = 0f;
        // Recessed "display case" base: darker than the surrounding panel so the slot grid and
        // native card frames read as a lit shelf inside a frame.
        _gridViewport.style.backgroundColor = Colors.CollectionGridCaseBackground;
        UiStyle.Radius(_gridViewport.style, Radii.Md);
        UiStyle.Border(_gridViewport.style, Borders.Thin, Colors.HistoryListFrameBorder);
        _gridViewport.style.overflow = Overflow.Hidden;
        parent.Add(_gridViewport);

        _gridScrollView = new ScrollView(ScrollViewMode.Vertical);
        _gridScrollView.style.flexGrow = 1f;
        _gridScrollView.style.flexShrink = 1f;
        _gridScrollView.style.minHeight = 0f;
        _gridScrollView.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
        _gridScrollView.verticalScrollerVisibility = ScrollerVisibility.Auto;
        _gridScrollView.mouseWheelScrollSize = CollectionGridConstants.MouseWheelScrollPoints;
        _gridScrollView.contentContainer.style.flexDirection = FlexDirection.Column;
        // Scroll offset is polled in CollectionPanel.Update via ReadScrollYPixels(): the
        // publicized UIElements Scroller exposes valueChanged ambiguously (field vs property),
        // so we avoid subscribing.
        _gridViewport.Add(_gridScrollView);

        _gridContentSpacer = new VisualElement();
        _gridContentSpacer.style.flexGrow = 0f;
        _gridContentSpacer.style.flexShrink = 0f;
        _gridContentSpacer.style.height = 1f;
        _gridContentSpacer.style.minHeight = 1f;
        _gridContentSpacer.style.width = Length.Percent(100f);
        _gridScrollView.contentContainer.Add(_gridContentSpacer);

        _emptyLabel = CreateLabel(
            Sizes.FontBody,
            FontStyle.Normal,
            Colors.HistoryFooterSecondaryText
        );
        _emptyLabel.text = CollectionPanelText.NoMatches();
        _emptyLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
        _emptyLabel.style.height = 80f;
        _emptyLabel.style.whiteSpace = WhiteSpace.Normal;
        _emptyLabel.style.overflow = Overflow.Hidden;
        _emptyLabel.style.display = DisplayStyle.None;
        _gridViewport.Add(_emptyLabel);

        _loadingLabel = CreateLabel(
            Sizes.FontBody,
            FontStyle.Bold,
            Colors.HistoryFooterSecondaryText
        );
        _loadingLabel.pickingMode = PickingMode.Ignore;
        _loadingLabel.style.position = Position.Absolute;
        _loadingLabel.style.left = 0f;
        _loadingLabel.style.right = 0f;
        _loadingLabel.style.top = 0f;
        _loadingLabel.style.bottom = 0f;
        _loadingLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
        _loadingLabel.style.whiteSpace = WhiteSpace.Normal;
        _loadingLabel.style.overflow = Overflow.Hidden;
        _loadingLabel.style.display = DisplayStyle.None;
        _gridViewport.Add(_loadingLabel);
    }
}
