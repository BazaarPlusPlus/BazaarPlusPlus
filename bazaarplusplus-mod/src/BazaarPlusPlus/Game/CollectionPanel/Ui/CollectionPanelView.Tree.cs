#nullable enable
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
        panel.style.backgroundColor = Colors.CollectionPanelBackground;
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
        rail.style.minWidth = Sizes.CollectionOperationRailMinWidth;
        rail.style.maxWidth = Sizes.OperationRailMaxWidth;
        rail.style.minHeight = 0f;
        rail.style.overflow = Overflow.Hidden;
        rail.style.marginLeft = UiSpacing.ColumnGap;
        parent.Add(rail);

        // The top controls read as one control deck: navigation and context first, followed by
        // the search tools. This keeps the catalog's controls from looking like separate rows.
        var controlDeck = new VisualElement();
        controlDeck.style.flexDirection = FlexDirection.Column;
        controlDeck.style.flexShrink = 0f;
        UiStyle.Padding(controlDeck.style, UiSpacing.Lg);
        ApplyCollectionFilterCardTreatment(controlDeck, Colors.CollectionFilterCardMutedPalette);
        rail.Add(controlDeck);

        // Title + Close (Close lives here in the operation area, not a top bar).
        var titleRow = new VisualElement();
        titleRow.style.flexDirection = FlexDirection.Row;
        titleRow.style.alignItems = Align.Center;
        titleRow.style.minHeight = Sizes.CollectionTabToggleHeight;
        controlDeck.Add(titleRow);

        _title = CreateLabel(Sizes.FontTitle, FontStyle.Normal, Colors.White);
        _title.style.flexGrow = 1f;
        _title.style.flexShrink = 1f;
        _title.style.minWidth = 0f;
        _title.style.whiteSpace = WhiteSpace.NoWrap;
        _title.style.overflow = Overflow.Hidden;
        titleRow.Add(_title);

        _closeButton = CreateCloseButton(_commands.Close);
        _closeButton.style.marginLeft = UiSpacing.Sm;
        titleRow.Add(_closeButton);

        // Keep the attribution row's own reserved height and wrap behavior: clamping it to chip
        // height with NoWrap hard-clips supporter names and the sponsor action.
        _subtitle = BPPSupporterAttributionRow.Create();
        _subtitle.style.overflow = Overflow.Hidden;
        controlDeck.Add(_subtitle);

        if (_stagingItemIdCopyEnabled)
        {
            _stagingIdCopyLabel = CreateLabel(
                Sizes.FontCorner,
                FontStyle.Normal,
                Colors.HistoryStatusText
            );
            _stagingIdCopyLabel.text = StagingIdCopyHint;
            _stagingIdCopyLabel.style.marginTop = UiSpacing.Xs;
            _stagingIdCopyLabel.style.whiteSpace = WhiteSpace.NoWrap;
            _stagingIdCopyLabel.style.overflow = Overflow.Hidden;
            controlDeck.Add(_stagingIdCopyLabel);
        }

        // The fixed-width toggles below (tab 192 + sort 120 + day/reset 88 plus margins) exceed
        // the rail's minimum content width, so the row must be allowed to wrap: trailing
        // controls flow to a second line instead of being clipped by the rail's hidden
        // overflow. Children carry the row's top margin so wrapped lines stay separated.
        var primaryControlsRow = CreateOperationRow(UiSpacing.None);
        primaryControlsRow.style.flexWrap = Wrap.Wrap;
        controlDeck.Add(primaryControlsRow);

        // The search field is deliberately persistent. Zero flex-basis keeps the wrap decision
        // independent of the typed query's width: the field only takes leftover space.
        _searchInputContainer = CreateSearchField();
        _searchInputContainer.style.flexBasis = 0f;
        _searchInputContainer.style.marginTop = UiSpacing.Md;
        primaryControlsRow.Add(_searchInputContainer);

        // Catalog choice sits between search and sort, matching the row height so the whole
        // line reads as one control strip.
        _tabModeControl = CreateFacetChoiceControl(
            CollectionPanelText.ItemsTab(),
            CollectionPanelText.SkillsTab(),
            () => _commands.SetActiveTab(CollectionTabKind.Items),
            () => _commands.SetActiveTab(CollectionTabKind.Skills),
            Sizes.CollectionTabToggleWidth,
            Sizes.CollectionSearchRowHeight,
            Sizes.FontBody,
            slanted: false
        );
        _tabModeControl.style.marginTop = UiSpacing.Md;
        primaryControlsRow.Add(_tabModeControl);

        primaryControlsRow.Add(CreateSortButtonGroup());

        _standardOperationControls = new VisualElement();
        _standardOperationControls.style.flexDirection = FlexDirection.Row;
        _standardOperationControls.style.alignItems = Align.Center;
        _standardOperationControls.style.flexWrap = Wrap.NoWrap;
        _standardOperationControls.style.flexShrink = 0f;
        _standardOperationControls.style.marginLeft = UiSpacing.Sm;
        _standardOperationControls.style.marginTop = UiSpacing.Md;
        primaryControlsRow.Add(_standardOperationControls);

        // Compact day-number icon toggle.
        _dayToggleButton = CreateDayToggleButton();
        _standardOperationControls.Add(_dayToggleButton);

        // Reset closes the operation row at the far right, away from the frequent controls.
        var resetButton = CreateResetButton(_commands.ResetFilters);
        resetButton.style.marginLeft = UiSpacing.Sm;
        _standardOperationControls.Add(resetButton);

        // The foundational hero/size/quality card is pinned between the control deck and the
        // scrolling filter stack: switching tabs or scrolling secondary filters must not move
        // the primary selectors.
        _heroFilterSection = CreateFilterSection(
            rail,
            CollectionPanelText.HeroHeader(),
            UiSpacing.Xl,
            out _heroChipRow,
            out _heroFilterLabel,
            out var heroHeaderRow,
            palette: Colors.CollectionFilterCardMutedPalette
        );
        _allHeroesButton = CreateTextMatchModeButton(
            "bpp-collection-all-heroes",
            _commands.ToggleAllHeroes
        );
        _allHeroesButton.style.marginLeft = UiSpacing.Sm;
        heroHeaderRow.Add(_allHeroesButton);
        _heroChipRow.style.flexWrap = Wrap.NoWrap;
        _heroChipRow.style.justifyContent = Justify.FlexStart;
        _heroChipRow.RegisterCallback<GeometryChangedEvent>(OnHeroChipRowGeometryChanged);

        // Keep hero, size, and quality in one visual card. The child section contributes only its
        // chip row, without introducing a nested card or a second heading.
        _tierFilterSection = CreateFilterSection(
            _heroFilterSection,
            string.Empty,
            UiSpacing.Lg,
            out var tierSizeChipRow,
            out _tierFilterLabel,
            out _,
            card: false
        );
        tierSizeChipRow.style.flexWrap = Wrap.NoWrap;
        tierSizeChipRow.style.justifyContent = Justify.FlexStart;
        // Quality leads the combined row: the size segment hides on the Skills tab, so putting
        // it second keeps the quality chips anchored across tab switches.
        _tierChipRow = CreateCombinedFilterChipSegment();
        _sizeChipRow = CreateCombinedFilterChipSegment();
        tierSizeChipRow.Add(_tierChipRow);
        tierSizeChipRow.Add(_sizeChipRow);
        _sizeChipRow.style.marginLeft = UiSpacing.Sm;
        _tierChipRow.style.flexWrap = Wrap.NoWrap;
        _tierChipRow.style.justifyContent = Justify.FlexStart;

        var controlsViewport = new VisualElement();
        controlsViewport.style.flexGrow = 1f;
        controlsViewport.style.flexShrink = 1f;
        controlsViewport.style.minHeight = 0f;
        controlsViewport.style.position = Position.Relative;
        controlsViewport.style.overflow = Overflow.Hidden;
        rail.Add(controlsViewport);

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
        _controlsDragScroller = new ScrollViewDragScroller(controlsScroll);
        controlsViewport.Add(controlsScroll);

        _controlsScrollShadow = CreateControlsScrollShadow();
        controlsViewport.Add(_controlsScrollShadow);

        // Keyword filter (EHiddenTag gameplay keywords). This is the common secondary filter for
        // Items and Skills, so keep it directly below Quality.
        _keywordFilterSection = CreateFilterSection(
            controlsScroll,
            CollectionPanelText.KeywordHeader(),
            UiSpacing.Lg,
            out _keywordChipRow,
            out _keywordFilterLabel,
            out var keywordHeaderRow,
            palette: Colors.CollectionFilterCardMutedPalette
        );
        _keywordMatchModeButton = CreateTextMatchModeControl(mode =>
            _commands.SetKeywordMatchMode(mode)
        );
        keywordHeaderRow.Add(_keywordMatchModeButton);
        _keywordChipRow.style.flexWrap = Wrap.Wrap;
        _keywordChipRow.style.justifyContent = Justify.FlexStart;

        // Player-facing type chips get their own titled card; the chip flow itself is unchanged.
        _tagFilterSection = CreateFilterChipSection(
            controlsScroll,
            CollectionPanelText.TagHeader(),
            UiSpacing.Lg,
            out _tagChipRow,
            out var tagHeaderRow,
            Colors.CollectionFilterCardMutedPalette
        );
        _tagMatchModeButton = CreateTextMatchModeControl(mode => _commands.SetTagMatchMode(mode));
        tagHeaderRow.Add(_tagMatchModeButton);
        _tagChipRow.style.flexWrap = Wrap.Wrap;
        _tagChipRow.style.justifyContent = Justify.FlexStart;

        // Source filter (merchant portraits on Items, trainer portraits on Skills).
        _sourceFilterSection = CreateFilterSection(
            controlsScroll,
            CollectionPanelText.SourceHeader(ECardType.Item),
            UiSpacing.Lg,
            out _sourceChipRow,
            out _sourceFilterLabel,
            palette: Colors.CollectionFilterCardMutedPalette
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
        controlsScroll.Add(_disclaimerLabel);

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
        row.style.flexWrap = Wrap.NoWrap;
        row.style.flexShrink = 0f;
        row.style.marginTop = marginTop;
        return row;
    }

    private static VisualElement CreateControlsScrollShadow()
    {
        var shadow = new VisualElement { pickingMode = PickingMode.Ignore };
        shadow.style.position = Position.Absolute;
        shadow.style.left = 0f;
        shadow.style.right = 0f;
        shadow.style.top = 0f;
        UiStyle.FixedHeight(shadow.style, Sizes.CollectionScrollShadowHeight);
        shadow.style.display = DisplayStyle.None;
        shadow.generateVisualContent += context => DrawControlsScrollShadow(context, shadow);
        return shadow;
    }

    private static void DrawControlsScrollShadow(
        MeshGenerationContext context,
        VisualElement shadow
    )
    {
        var rect = shadow.contentRect;
        if (rect.width <= 0f || rect.height <= 0f)
            return;

        var top = Colors.WithAlpha(Colors.CollectionPanelBackground, 0.92f);
        var bottom = Colors.Clear;
        var mesh = context.Allocate(4, 6);
        mesh.SetNextVertex(
            new Vertex { position = new Vector3(rect.xMin, rect.yMin, Vertex.nearZ), tint = top }
        );
        mesh.SetNextVertex(
            new Vertex { position = new Vector3(rect.xMax, rect.yMin, Vertex.nearZ), tint = top }
        );
        mesh.SetNextVertex(
            new Vertex { position = new Vector3(rect.xMax, rect.yMax, Vertex.nearZ), tint = bottom }
        );
        mesh.SetNextVertex(
            new Vertex { position = new Vector3(rect.xMin, rect.yMax, Vertex.nearZ), tint = bottom }
        );
        mesh.SetNextIndex(0);
        mesh.SetNextIndex(1);
        mesh.SetNextIndex(2);
        mesh.SetNextIndex(0);
        mesh.SetNextIndex(2);
        mesh.SetNextIndex(3);
    }

    // The treatment is painted on the card itself, so hierarchy can be expressed through a
    // stronger control deck and quiet secondary filters without changing their contents.
    private static void ApplyCollectionFilterCardTreatment(
        VisualElement card,
        CollectionFilterCardPalette? palette = null
    )
    {
        var resolvedPalette = palette ?? Colors.CollectionFilterCardReferencePalette;
        card.style.backgroundColor = resolvedPalette.Background;
        UiStyle.BorderWidth(card.style, Borders.None);
        UiStyle.Radius(card.style, Radii.Panel);
        card.style.overflow = Overflow.Hidden;
        card.generateVisualContent += context =>
            DrawCollectionFilterCardBackground(context, card, resolvedPalette);
    }

    private static void DrawCollectionFilterCardBackground(
        MeshGenerationContext context,
        VisualElement card,
        CollectionFilterCardPalette palette
    )
    {
        // contentRect excludes the card's padding. These cards deliberately have 16–24px of
        // padding, so a border based on it becomes the inset frame shown in the screenshot.
        // paddingRect is the card's full local paint box here: we own the border and set its
        // USS border width to zero in ApplyCollectionFilterCardTreatment.
        var rect = card.paddingRect;
        if (rect.width <= 0f || rect.height <= 0f)
            return;

        DrawCollectionFilterCardGlow(
            context,
            rect,
            new Vector2(rect.xMin + rect.width * 0.13f, rect.yMin + rect.height * 0.08f),
            rect.width * 0.68f,
            rect.height * 1.05f,
            palette.TopGlow
        );
        DrawCollectionFilterCardGlow(
            context,
            rect,
            new Vector2(rect.xMax - rect.width * 0.05f, rect.yMax - rect.height * 0.05f),
            rect.width * 0.62f,
            rect.height * 0.92f,
            palette.BottomGlow
        );
        if (palette.Decoration.a > 0f)
            DrawCollectionFilterCardDotField(context, rect, palette.Decoration);
        DrawCollectionFilterCardGradientBorder(context, rect, palette);
    }

    private static void DrawCollectionFilterCardGradientBorder(
        MeshGenerationContext context,
        Rect rect,
        CollectionFilterCardPalette palette
    )
    {
        // The reference's outline is a quiet, single-pixel highlight. A filled mesh ring makes
        // its full width visible and reads as a neon frame, so draw a stroked path instead.
        const float strokeWidth = 1f;
        const int edgeSegments = 12;
        const int cornerSegments = 7;
        var inset = strokeWidth * 0.5f;
        var bounds = new Rect(
            rect.xMin + inset,
            rect.yMin + inset,
            rect.width - strokeWidth,
            rect.height - strokeWidth
        );
        if (bounds.width <= strokeWidth * 2f || bounds.height <= strokeWidth * 2f)
            return;

        var radius = Mathf.Min(
            Radii.Panel - inset,
            Mathf.Min(bounds.width, bounds.height) * 0.5f
        );
        var painter = context.painter2D;
        painter.lineWidth = strokeWidth;
        painter.lineCap = LineCap.Round;
        painter.lineJoin = LineJoin.Round;

        var first = new Vector2(bounds.xMin + radius, bounds.yMin);
        var previous = first;

        void StrokeTo(Vector2 next)
        {
            painter.strokeColor = CollectionFilterCardBorderColor(
                (previous + next) * 0.5f,
                bounds,
                palette
            );
            painter.BeginPath();
            painter.MoveTo(previous);
            painter.LineTo(next);
            painter.Stroke();
            previous = next;
        }

        void StrokeStraightTo(Vector2 destination)
        {
            var start = previous;
            for (var step = 1; step <= edgeSegments; step++)
                StrokeTo(Vector2.Lerp(start, destination, step / (float)edgeSegments));
        }

        void StrokeCorner(int corner, float startDegrees)
        {
            var center = CollectionFilterCardCornerCenter(bounds, radius, corner);
            for (var step = 1; step <= cornerSegments; step++)
            {
                var radians = (startDegrees + 90f * step / cornerSegments) * Mathf.Deg2Rad;
                StrokeTo(center + new Vector2(Mathf.Cos(radians), Mathf.Sin(radians)) * radius);
            }
        }

        // Walk the entire perimeter clockwise. Each short stroke gets a colour sampled from the
        // four-corner gradient, which keeps the transition smooth without ever creating a band.
        StrokeStraightTo(new Vector2(bounds.xMax - radius, bounds.yMin));
        StrokeCorner(0, -90f);
        StrokeStraightTo(new Vector2(bounds.xMax, bounds.yMax - radius));
        StrokeCorner(1, 0f);
        StrokeStraightTo(new Vector2(bounds.xMin + radius, bounds.yMax));
        StrokeCorner(2, 90f);
        StrokeStraightTo(new Vector2(bounds.xMin, bounds.yMin + radius));
        StrokeCorner(3, 180f);
    }

    private static Vector2 CollectionFilterCardCornerCenter(Rect rect, float radius, int corner) =>
        corner switch
        {
            0 => new Vector2(rect.xMax - radius, rect.yMin + radius),
            1 => new Vector2(rect.xMax - radius, rect.yMax - radius),
            2 => new Vector2(rect.xMin + radius, rect.yMax - radius),
            _ => new Vector2(rect.xMin + radius, rect.yMin + radius),
        };

    private static Color CollectionFilterCardBorderColor(
        Vector2 point,
        Rect bounds,
        CollectionFilterCardPalette palette
    )
    {
        var horizontal = Mathf.InverseLerp(bounds.xMin, bounds.xMax, point.x);
        var vertical = Mathf.InverseLerp(bounds.yMin, bounds.yMax, point.y);
        var top = Color.Lerp(
            palette.BorderTopLeft,
            palette.BorderTopRight,
            horizontal
        );
        var bottom = Color.Lerp(
            palette.BorderBottomLeft,
            palette.BorderBottomRight,
            horizontal
        );
        return Color.Lerp(top, bottom, vertical);
    }

    private static void DrawCollectionFilterCardGlow(
        MeshGenerationContext context,
        Rect rect,
        Vector2 center,
        float radiusX,
        float radiusY,
        Color color
    )
    {
        radiusX = Mathf.Min(radiusX, rect.width);
        radiusY = Mathf.Min(radiusY, rect.height);
        var edge = Colors.WithAlpha(color, 0f);
        var mesh = context.Allocate(5, 12);
        mesh.SetNextVertex(
            new Vertex { position = new Vector3(center.x, center.y, Vertex.nearZ), tint = color }
        );
        mesh.SetNextVertex(
            new Vertex
            {
                position = new Vector3(center.x, center.y - radiusY, Vertex.nearZ),
                tint = edge,
            }
        );
        mesh.SetNextVertex(
            new Vertex
            {
                position = new Vector3(center.x + radiusX, center.y, Vertex.nearZ),
                tint = edge,
            }
        );
        mesh.SetNextVertex(
            new Vertex
            {
                position = new Vector3(center.x, center.y + radiusY, Vertex.nearZ),
                tint = edge,
            }
        );
        mesh.SetNextVertex(
            new Vertex
            {
                position = new Vector3(center.x - radiusX, center.y, Vertex.nearZ),
                tint = edge,
            }
        );
        mesh.SetNextIndex(0);
        mesh.SetNextIndex(1);
        mesh.SetNextIndex(2);
        mesh.SetNextIndex(0);
        mesh.SetNextIndex(2);
        mesh.SetNextIndex(3);
        mesh.SetNextIndex(0);
        mesh.SetNextIndex(3);
        mesh.SetNextIndex(4);
        mesh.SetNextIndex(0);
        mesh.SetNextIndex(4);
        mesh.SetNextIndex(1);
    }

    private static void DrawCollectionFilterCardDotField(
        MeshGenerationContext context,
        Rect rect,
        Color decoration
    )
    {
        const int columns = 10;
        const int rows = 6;
        var spacing = Mathf.Clamp(Mathf.Min(rect.width, rect.height) * 0.055f, 4f, 8f);
        var dotSize = Mathf.Clamp(spacing * 0.19f, 1.1f, 1.7f);
        var left = rect.xMax - spacing * (columns + 1.5f);
        var top = rect.yMax - spacing * (rows + 1.2f);
        var mesh = context.Allocate(columns * rows * 4, columns * rows * 6);
        for (var row = 0; row < rows; row++)
        {
            for (var column = 0; column < columns; column++)
            {
                var strength = ((column + 1f) / columns) * ((row + 1f) / rows);
                var color = Colors.WithAlpha(
                    decoration,
                    decoration.a * (0.2f + strength * 0.8f)
                );
                var x = left + column * spacing;
                var y = top + row * spacing;
                mesh.SetNextVertex(
                    new Vertex { position = new Vector3(x, y, Vertex.nearZ), tint = color }
                );
                mesh.SetNextVertex(
                    new Vertex
                    {
                        position = new Vector3(x + dotSize, y, Vertex.nearZ),
                        tint = color,
                    }
                );
                mesh.SetNextVertex(
                    new Vertex
                    {
                        position = new Vector3(x + dotSize, y + dotSize, Vertex.nearZ),
                        tint = color,
                    }
                );
                mesh.SetNextVertex(
                    new Vertex
                    {
                        position = new Vector3(x, y + dotSize, Vertex.nearZ),
                        tint = color,
                    }
                );
            }
        }

        for (var dot = 0; dot < columns * rows; dot++)
        {
            var vertex = dot * 4;
            mesh.SetNextIndex((ushort)vertex);
            mesh.SetNextIndex((ushort)(vertex + 1));
            mesh.SetNextIndex((ushort)(vertex + 2));
            mesh.SetNextIndex((ushort)vertex);
            mesh.SetNextIndex((ushort)(vertex + 2));
            mesh.SetNextIndex((ushort)(vertex + 3));
        }
    }

    private static VisualElement CreateOperationSpacer()
    {
        var spacer = new VisualElement();
        spacer.style.flexGrow = 1f;
        spacer.style.flexShrink = 1f;
        spacer.style.minWidth = UiSpacing.Md;
        return spacer;
    }

    private static Button CreateCloseButton(Action onClick)
    {
        var button = CreateButton(
            string.Empty,
            onClick,
            Sizes.CollectionCloseButtonSize,
            Sizes.CollectionCloseButtonSize
        );
        button.tooltip = CollectionPanelText.Close();
        StyleCollectionChip(button, Colors.CollectionChipBackground, Colors.CollectionChipText);
        UiStyle.Radius(button.style, Sizes.CollectionCloseButtonSize / 2f);

        var icon = new VisualElement { pickingMode = PickingMode.Ignore };
        UiStyle.FixedSize(icon.style, Sizes.CollectionCloseIconSize, Sizes.CollectionCloseIconSize);
        icon.style.color = Colors.CollectionChipText;
        icon.generateVisualContent += context => DrawCloseIcon(context, icon);
        button.Add(icon);
        return button;
    }

    private static Button CreateResetButton(Action onClick)
    {
        var button = CreateButton(
            string.Empty,
            onClick,
            Sizes.CollectionSearchRowHeight,
            Sizes.CollectionSearchRowHeight
        );
        button.tooltip = CollectionPanelText.ResetFilters();
        StyleButton(button, Colors.CollectionChipBackground, Colors.CollectionChipText);
        UiStyle.Radius(button.style, Radii.CollectionChip);
        UiStyle.Border(button.style, Borders.Thin, Colors.CollectionChipBorder);

        var icon = new VisualElement { pickingMode = PickingMode.Ignore };
        UiStyle.FixedSize(icon.style, 18f, 18f);
        icon.style.marginTop = -0.5f;
        icon.style.color = Colors.WithAlpha(Colors.CollectionChipText, 0.84f);
        icon.generateVisualContent += context => DrawResetIcon(context, icon);
        button.Add(icon);
        return button;
    }

    private static void DrawCloseIcon(MeshGenerationContext context, VisualElement icon)
    {
        var rect = icon.contentRect;
        if (rect.width <= 0f || rect.height <= 0f)
            return;

        var inset = Mathf.Min(rect.width, rect.height) * 0.22f;
        var painter = context.painter2D;
        painter.lineWidth = Mathf.Max(1.5f, rect.width * 0.14f);
        painter.lineCap = LineCap.Round;
        painter.strokeColor = icon.resolvedStyle.color;

        painter.BeginPath();
        painter.MoveTo(new Vector2(rect.xMin + inset, rect.yMin + inset));
        painter.LineTo(new Vector2(rect.xMax - inset, rect.yMax - inset));
        painter.Stroke();

        painter.BeginPath();
        painter.MoveTo(new Vector2(rect.xMax - inset, rect.yMin + inset));
        painter.LineTo(new Vector2(rect.xMin + inset, rect.yMax - inset));
        painter.Stroke();
    }

    private static void DrawResetIcon(MeshGenerationContext context, VisualElement icon)
    {
        var rect = icon.contentRect;
        if (rect.width <= 0f || rect.height <= 0f)
            return;

        var center = rect.center;
        var radius = Mathf.Min(rect.width, rect.height) * 0.32f;
        var painter = context.painter2D;
        painter.lineWidth = Mathf.Max(1.35f, rect.width * 0.115f);
        painter.lineCap = LineCap.Round;
        painter.lineJoin = LineJoin.Round;
        painter.strokeColor = icon.resolvedStyle.color;
        painter.BeginPath();
        painter.MoveTo(new Vector2(center.x - radius * 0.82f, center.y - radius * 0.18f));
        painter.LineTo(new Vector2(center.x - radius * 0.44f, center.y - radius * 0.76f));
        painter.LineTo(new Vector2(center.x + radius * 0.26f, center.y - radius * 0.88f));
        painter.LineTo(new Vector2(center.x + radius * 0.82f, center.y - radius * 0.40f));
        painter.LineTo(new Vector2(center.x + radius * 0.82f, center.y + radius * 0.36f));
        painter.LineTo(new Vector2(center.x + radius * 0.26f, center.y + radius * 0.84f));
        painter.LineTo(new Vector2(center.x - radius * 0.46f, center.y + radius * 0.68f));
        painter.Stroke();

        painter.BeginPath();
        painter.MoveTo(new Vector2(center.x - radius * 0.16f, center.y - radius * 0.92f));
        painter.LineTo(new Vector2(center.x - radius * 0.86f, center.y - radius * 0.18f));
        painter.LineTo(new Vector2(center.x - radius * 0.02f, center.y - radius * 0.10f));
        painter.Stroke();
    }

    private static void DrawSortIcon(MeshGenerationContext context, VisualElement icon)
    {
        var rect = icon.contentRect;
        if (rect.width <= 0f || rect.height <= 0f)
            return;

        var painter = context.painter2D;
        painter.lineWidth = Mathf.Max(1.2f, rect.width * 0.1f);
        painter.lineCap = LineCap.Round;
        painter.strokeColor = icon.resolvedStyle.color;

        var left = rect.xMin + rect.width * 0.12f;
        var fullWidth = rect.width * 0.76f;
        var firstY = rect.yMin + rect.height * 0.25f;
        var secondY = rect.center.y;
        var thirdY = rect.yMax - rect.height * 0.25f;
        painter.BeginPath();
        painter.MoveTo(new Vector2(left, firstY));
        painter.LineTo(new Vector2(left + fullWidth, firstY));
        painter.Stroke();

        painter.BeginPath();
        painter.MoveTo(new Vector2(left, secondY));
        painter.LineTo(new Vector2(left + fullWidth * 0.76f, secondY));
        painter.Stroke();

        painter.BeginPath();
        painter.MoveTo(new Vector2(left, thirdY));
        painter.LineTo(new Vector2(left + fullWidth * 0.52f, thirdY));
        painter.Stroke();
    }

    private VisualElement CreateSearchField()
    {
        var container = new VisualElement();
        container.style.flexDirection = FlexDirection.Row;
        container.style.flexGrow = 1f;
        container.style.flexShrink = 1f;
        container.style.minWidth = 0f;
        container.style.height = Sizes.CollectionSearchRowHeight;
        container.pickingMode = PickingMode.Position;

        var frame = new VisualElement();
        frame.style.flexDirection = FlexDirection.Row;
        frame.style.alignItems = Align.Center;
        frame.style.flexGrow = 1f;
        frame.style.flexShrink = 1f;
        frame.style.minWidth = 0f;
        frame.style.height = Sizes.CollectionSearchRowHeight;
        frame.style.backgroundColor = Colors.CollectionChipBackground;
        UiStyle.Border(frame.style, Borders.Thin, Colors.CollectionChipBorder);
        UiStyle.Radius(frame.style, Radii.CollectionChip);
        UiStyle.HorizontalPadding(frame.style, UiSpacing.Md);
        _searchFrame = frame;
        container.Add(frame);

        var field = new TextField { label = string.Empty };
        _searchField = field;
        field.textSelection.selectAllOnFocus = false;
        field.textSelection.selectAllOnMouseUp = false;
        field.tooltip = CollectionPanelText.SearchTooltip();
        field.style.flexGrow = 1f;
        field.style.flexShrink = 1f;
        field.style.minWidth = 0f;
        field.style.height = Length.Percent(100f);
        field.style.backgroundColor = Color.clear;
        field.style.color = Colors.CollectionChipText;
        _typography!.Apply(field);
        field.style.fontSize = Sizes.CollectionTagFontSize;
        field.style.borderLeftWidth = 0f;
        field.style.borderRightWidth = 0f;
        field.style.borderTopWidth = 0f;
        field.style.borderBottomWidth = 0f;
        UiStyle.Padding(field.style, UiSpacing.None, UiSpacing.None);
        frame.Add(field);

        _searchPlaceholderLabel = CreateLabel(
            Sizes.CollectionTagFontSize,
            FontStyle.Normal,
            Colors.WithAlpha(Colors.CollectionChipText, 0.58f)
        );
        _searchPlaceholderLabel.pickingMode = PickingMode.Ignore;
        _searchPlaceholderLabel.style.position = Position.Absolute;
        _searchPlaceholderLabel.style.left = UiSpacing.Md;
        _searchPlaceholderLabel.style.right = UiSpacing.Md;
        _searchPlaceholderLabel.style.top = 0f;
        _searchPlaceholderLabel.style.bottom = 0f;
        _searchPlaceholderLabel.style.whiteSpace = WhiteSpace.NoWrap;
        _searchPlaceholderLabel.style.overflow = Overflow.Hidden;
        _searchPlaceholderLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
        frame.Add(_searchPlaceholderLabel);

        field.RegisterValueChangedCallback(evt =>
        {
            RefreshSearchPlaceholder(evt.newValue);
            _commands.SetSearchQuery(evt.newValue);
        });
        var hovered = false;
        var focused = false;
        void RefreshFrame()
        {
            var background = Colors.CollectionChipBackground;
            var border = Colors.CollectionChipBorder;
            if (focused)
            {
                ApplySearchFocusPulse();
                return;
            }
            else if (hovered)
            {
                background = Colors.CollectionChipHoverBackground;
                border = Colors.CollectionChipHoverBorder;
            }

            frame.style.backgroundColor = background;
            UiStyle.BorderColor(frame.style, border);
        }

        field.RegisterCallback<MouseEnterEvent>(_ =>
        {
            hovered = true;
            RefreshFrame();
        });
        field.RegisterCallback<MouseLeaveEvent>(_ =>
        {
            hovered = false;
            RefreshFrame();
        });
        field.RegisterCallback<FocusInEvent>(_ =>
        {
            focused = true;
            _searchFocused = true;
            _searchFocusPulseElapsed = 0f;
            RefreshFrame();
        });
        field.RegisterCallback<FocusOutEvent>(_ =>
        {
            focused = false;
            _searchFocused = false;
            _searchFocusPulseElapsed = 0f;
            RefreshFrame();
        });
        RefreshFrame();

        field.RegisterCallback<GeometryChangedEvent>(_ => StyleSearchField(field));
        return container;
    }

    private void TickSearchFocusPulse(float deltaSeconds)
    {
        if (!_searchFocused || _searchFrame == null)
            return;

        _searchFocusPulseElapsed += deltaSeconds;
        ApplySearchFocusPulse();
    }

    private void ApplySearchFocusPulse()
    {
        if (_searchFrame == null)
            return;

        const float pulseSeconds = 2.4f;
        var phase = _searchFocusPulseElapsed * (Mathf.PI * 2f / pulseSeconds) - Mathf.PI * 0.5f;
        var pulse = Mathf.SmoothStep(0f, 1f, (Mathf.Sin(phase) + 1f) * 0.5f);
        _searchFrame.style.backgroundColor = Color.Lerp(
            Colors.CollectionChipSelectedBackground,
            Colors.CollectionChipSelectedHoverBackground,
            pulse
        );
        UiStyle.BorderColor(
            _searchFrame.style,
            Color.Lerp(
                Colors.CollectionChipSelectedBorder,
                Colors.CollectionChipSelectedHoverBorder,
                pulse
            )
        );
    }

    private VisualElement CreateSortButtonGroup()
    {
        var group = new VisualElement();
        group.style.flexDirection = FlexDirection.Row;
        group.style.alignItems = Align.Stretch;
        group.style.flexShrink = 0f;
        group.style.height = Sizes.CollectionSearchRowHeight;
        group.style.overflow = Overflow.Hidden;
        UiStyle.FixedWidth(group.style, CurrentSortActiveWidth() + CurrentSortInactiveWidth());
        UiStyle.Radius(group.style, Radii.CollectionChip);
        UiStyle.Border(group.style, Borders.Thin, Colors.CollectionChipBorder);
        group.style.marginLeft = UiSpacing.Sm;
        group.style.marginTop = UiSpacing.Md;

        _sortQualityButton = CreateInlineSortButton(
            SortQualityButtonName,
            CollectionPanelText.SortQuality(),
            () => _commands.SetSortPriority(CollectionSortPriority.Quality)
        );
        _sortSizeButton = CreateInlineSortButton(
            SortSizeButtonName,
            CollectionPanelText.SortSize(),
            () => _commands.SetSortPriority(CollectionSortPriority.Size)
        );
        group.Add(_sortQualityButton);
        group.Add(_sortSizeButton);
        group.Add(CreateFacetChoiceDivider(CurrentSortActiveWidth()));
        return group;
    }

    private void StyleSearchField(TextField field)
    {
        var label = field.Q<Label>();
        if (label != null)
        {
            label.style.display = DisplayStyle.None;
        }

        var input = field.Q(TextField.textInputUssName);
        if (input != null)
        {
            input.style.flexGrow = 1f;
            input.style.height = Length.Percent(100f);
            input.style.alignSelf = Align.Stretch;
            input.style.backgroundColor = Color.clear;
            input.style.color = Colors.CollectionChipText;
            _typography!.Apply(input);
            input.style.fontSize = Sizes.CollectionTagFontSize;
            input.style.unityTextAlign = TextAnchor.MiddleLeft;
            input.style.borderLeftWidth = 0f;
            input.style.borderRightWidth = 0f;
            input.style.borderTopWidth = 0f;
            input.style.borderBottomWidth = 0f;
            input.style.marginLeft = UiSpacing.None;
            input.style.marginRight = UiSpacing.None;
            input.style.marginTop = UiSpacing.None;
            input.style.marginBottom = UiSpacing.None;
            UiStyle.Padding(input.style, UiSpacing.None, UiSpacing.None);
        }

        var text = input?.Q<TextElement>();
        if (text != null)
        {
            text.style.flexGrow = 1f;
            text.style.height = Length.Percent(100f);
            text.style.alignSelf = Align.Stretch;
            text.style.color = Colors.CollectionChipText;
            _typography!.Apply(text);
            text.style.fontSize = Sizes.CollectionTagFontSize;
            text.style.unityTextAlign = TextAnchor.MiddleLeft;
        }
    }

    private static Button CreateInlineSortButton(string name, string text, Action onClick)
    {
        var button = CreateButton(
            string.Empty,
            onClick,
            CurrentSortInactiveWidth(),
            Sizes.CollectionSearchRowHeight,
            fixedWidth: false
        );
        button.name = name;
        button.style.flexShrink = 0f;
        button.style.flexDirection = FlexDirection.Row;
        button.style.justifyContent = Justify.Center;
        button.style.alignItems = Align.Center;
        UiStyle.HorizontalPadding(button.style, UiSpacing.Xxs);

        var icon = new VisualElement
        {
            name = SortButtonIconName,
            pickingMode = PickingMode.Ignore,
        };
        UiStyle.FixedSize(icon.style, Sizes.CollectionSortIconSize, Sizes.CollectionSortIconSize);
        icon.style.display = DisplayStyle.None;
        icon.style.flexShrink = 0f;
        icon.style.marginRight = UiSpacing.Xxs;
        icon.style.color = Colors.CollectionChipSelectedText;
        icon.generateVisualContent += context => DrawSortIcon(context, icon);
        button.Add(icon);

        var label = new Label(text)
        {
            name = SortButtonLabelName,
            pickingMode = PickingMode.Ignore,
        };
        label.style.fontSize = Sizes.CollectionTagFontSize;
        label.style.flexShrink = 1f;
        label.style.minWidth = 0f;
        label.style.whiteSpace = WhiteSpace.NoWrap;
        label.style.overflow = Overflow.Hidden;
        label.style.unityTextAlign = TextAnchor.MiddleCenter;
        label.style.color = Colors.CollectionChipText;
        button.Add(label);

        StyleCollectionGroupChip(
            button,
            Colors.CollectionChipBackground,
            Colors.CollectionChipText
        );
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
        out Label label,
        bool card = true,
        CollectionFilterCardPalette? palette = null
    ) =>
        CreateFilterSection(
            parent,
            title,
            marginTop,
            out chipRow,
            out label,
            out _,
            card,
            palette
        );

    private static VisualElement CreateFilterSection(
        VisualElement parent,
        string title,
        float marginTop,
        out VisualElement chipRow,
        out Label label,
        out VisualElement headerRow,
        bool card = true,
        CollectionFilterCardPalette? palette = null
    )
    {
        var section = new VisualElement();
        section.style.flexDirection = FlexDirection.Column;
        section.style.flexShrink = 0f;
        section.style.marginTop = marginTop;
        if (card)
        {
            UiStyle.Padding(section.style, UiSpacing.Md);
            ApplyCollectionFilterCardTreatment(section, palette);
        }
        parent.Add(section);

        headerRow = new VisualElement();
        headerRow.style.flexDirection = FlexDirection.Row;
        headerRow.style.alignItems = Align.Center;
        headerRow.style.alignSelf = Align.Stretch;
        headerRow.style.marginBottom = UiSpacing.Sm;
        if (string.IsNullOrEmpty(title))
            headerRow.style.display = DisplayStyle.None;
        section.Add(headerRow);

        label = CreateLabel(
            Sizes.CollectionFilterTitleFontSize,
            FontStyle.Bold,
            Colors.CollectionFilterTitleText
        );
        label.text = title;
        label.style.flexGrow = 1f;
        label.style.flexShrink = 1f;
        label.style.minWidth = 0f;
        label.style.marginLeft = UiSpacing.Xs;
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

    private VisualElement CreateTextMatchModeControl(Action<CollectionFacetMatchMode> onSelect)
    {
        var control = new VisualElement();
        control.style.flexDirection = FlexDirection.Row;
        control.style.flexShrink = 0f;
        control.style.alignItems = Align.Center;
        control.style.marginLeft = UiSpacing.Sm;
        control.Add(
            CreateTextMatchModeButton(
                FacetMatchAnyName,
                () => onSelect(CollectionFacetMatchMode.Any)
            )
        );
        control.Add(
            CreateTextMatchModeButton(
                FacetMatchAllName,
                () => onSelect(CollectionFacetMatchMode.All)
            )
        );
        return control;
    }

    private Button CreateTextMatchModeButton(string name, Action onClick)
    {
        var button = new Button(onClick) { name = name };
        _typography!.Apply(button);
        button.style.height = Sizes.FacetModeToggleHeight;
        button.style.flexShrink = 0f;
        button.style.fontSize = Sizes.FacetModeFontSize;
        button.style.unityTextAlign = TextAnchor.MiddleCenter;
        UiStyle.Padding(button.style, UiSpacing.Xs, UiSpacing.None);
        button.style.backgroundColor = Color.clear;
        UiStyle.BorderWidth(button.style, Borders.None);
        UiStyle.Radius(button.style, 0f);
        return button;
    }

    private static VisualElement CreateFacetChoiceControl(
        string firstText,
        string secondText,
        Action onFirst,
        Action onSecond,
        float segmentWidth,
        float height,
        int fontSize,
        bool slanted
    )
    {
        var control = new VisualElement();
        control.style.flexDirection = FlexDirection.Row;
        control.style.flexShrink = 0f;
        control.style.height = height;
        UiStyle.FixedWidth(control.style, segmentWidth * 2f);
        control.style.marginLeft = UiSpacing.Sm;
        control.style.overflow = Overflow.Hidden;
        UiStyle.Radius(control.style, Radii.CollectionChip);
        if (!slanted)
            UiStyle.Border(control.style, Borders.Thin, Colors.CollectionChipBorder);

        var any = CreateFacetMatchModeSegment(
            FacetMatchAnyName,
            firstText,
            onFirst,
            left: true,
            segmentWidth,
            height,
            fontSize,
            slanted
        );
        var all = CreateFacetMatchModeSegment(
            FacetMatchAllName,
            secondText,
            onSecond,
            left: false,
            segmentWidth,
            height,
            fontSize,
            slanted
        );
        control.Add(any);
        control.Add(all);
        if (!slanted)
            control.Add(CreateFacetChoiceDivider(segmentWidth));
        return control;
    }

    private static Button CreateFacetMatchModeSegment(
        string name,
        string text,
        Action onClick,
        bool left,
        float width,
        float height,
        int fontSize,
        bool slanted
    )
    {
        var button = CreateButton(string.Empty, onClick, width, height);
        button.name = name;
        button.style.flexShrink = 0f;
        if (!slanted)
            UiStyle.Padding(button.style, UiSpacing.Xs, UiSpacing.Sm);

        var label = new Label(text)
        {
            name = FacetMatchLabelName,
            pickingMode = PickingMode.Ignore,
        };
        label.style.fontSize = fontSize;
        label.style.unityTextAlign = TextAnchor.MiddleCenter;
        label.style.flexGrow = 1f;
        label.style.flexShrink = 1f;
        label.style.minWidth = 0f;
        label.style.whiteSpace = WhiteSpace.NoWrap;
        label.style.overflow = Overflow.Hidden;
        button.Add(label);

        StyleFacetMatchModeSegment(
            button,
            selected: false,
            left: left,
            fontSize: fontSize,
            slanted: slanted
        );
        return button;
    }

    private static VisualElement CreateFacetChoiceDivider(float left)
    {
        var divider = new VisualElement
        {
            name = FacetChoiceDividerName,
            pickingMode = PickingMode.Ignore,
        };
        divider.style.position = Position.Absolute;
        divider.style.left = left;
        divider.style.top = 0f;
        divider.style.bottom = 0f;
        divider.style.width = Borders.Thin;
        divider.style.backgroundColor = Colors.CollectionChipBorder;
        return divider;
    }

    private static VisualElement CreateCombinedFilterChipSegment()
    {
        var segment = new VisualElement();
        segment.style.flexDirection = FlexDirection.Row;
        segment.style.flexWrap = Wrap.NoWrap;
        segment.style.alignItems = Align.Center;
        segment.style.flexGrow = 0f;
        segment.style.flexShrink = 0f;
        segment.style.minWidth = 0f;
        segment.style.overflow = Overflow.Hidden;
        UiStyle.Radius(segment.style, Radii.CollectionChip);
        UiStyle.Border(segment.style, Borders.Thin, Colors.CollectionChipBorder);
        return segment;
    }

    private static VisualElement CreateChipGroupDivider()
    {
        var divider = new VisualElement { pickingMode = PickingMode.Ignore };
        divider.style.width = Borders.Thin;
        divider.style.height = Sizes.CollectionTagChipHeight;
        divider.style.flexShrink = 0f;
        divider.style.backgroundColor = Colors.CollectionChipBorder;
        return divider;
    }

    private static VisualElement CreateFilterChipSection(
        VisualElement parent,
        string title,
        float marginTop,
        out VisualElement chipRow,
        out VisualElement header,
        CollectionFilterCardPalette? palette = null
    )
    {
        var section = new VisualElement();
        section.style.flexDirection = FlexDirection.Column;
        section.style.flexShrink = 0f;
        section.style.marginTop = marginTop;
        UiStyle.Padding(section.style, UiSpacing.Md);
        ApplyCollectionFilterCardTreatment(section, palette);
        parent.Add(section);

        header = new VisualElement();
        header.style.flexDirection = FlexDirection.Row;
        header.style.alignItems = Align.Center;
        header.style.marginBottom = UiSpacing.Sm;
        section.Add(header);

        var label = CreateLabel(
            Sizes.CollectionFilterTitleFontSize,
            FontStyle.Bold,
            Colors.CollectionFilterTitleText
        );
        label.text = title;
        label.style.flexGrow = 1f;
        label.style.flexShrink = 1f;
        label.style.minWidth = 0f;
        label.style.marginLeft = UiSpacing.Xs;
        label.style.whiteSpace = WhiteSpace.NoWrap;
        header.Add(label);

        chipRow = CreateFilterChipRow();
        section.Add(chipRow);
        return section;
    }

    private static VisualElement CreateFilterChipRow()
    {
        var row = new VisualElement();
        row.style.flexDirection = FlexDirection.Row;
        row.style.flexWrap = Wrap.Wrap;
        row.style.alignItems = Align.Center;
        row.style.alignSelf = Align.Stretch;
        return row;
    }

    // Compact day-number "icon": shows the current run day (or an unavailable dash) and toggles
    // whether the day participates in filtering. The tooltip names the number-only control.
    private Button CreateDayToggleButton()
    {
        var button = CreateButton(
            string.Empty,
            _commands.ToggleRunDayFilter,
            Sizes.DayIconWidth,
            Sizes.CollectionSearchRowHeight
        );
        button.tooltip = CollectionPanelText.DayHeader();
        var content = new VisualElement { pickingMode = PickingMode.Ignore };
        content.style.flexDirection = FlexDirection.Column;
        content.style.alignItems = Align.Center;
        content.style.justifyContent = Justify.Center;
        content.style.flexGrow = 1f;
        _dayToggleContent = content;
        var caption = new Label(CollectionPanelText.DayCaption())
        {
            pickingMode = PickingMode.Ignore,
        };
        caption.style.fontSize = Sizes.FontTiny;
        caption.style.marginBottom = -2f;
        caption.style.unityTextAlign = TextAnchor.MiddleCenter;
        caption.style.color = Colors.CollectionChipText;
        var value = new Label { pickingMode = PickingMode.Ignore };
        value.style.fontSize = Sizes.FontButton;
        value.style.unityTextAlign = TextAnchor.MiddleCenter;
        value.style.color = Colors.CollectionChipText;
        _dayToggleCaption = caption;
        _dayToggleValue = value;
        content.Add(caption);
        content.Add(value);
        button.Add(content);
        StyleCollectionChip(button, Colors.CollectionChipBackground, Colors.CollectionChipText);
        return button;
    }

    private void BuildGrid(VisualElement parent)
    {
        _gridViewport = new VisualElement();
        _gridViewport.style.flexGrow = 1f;
        _gridViewport.style.flexShrink = 1f;
        _gridViewport.style.minHeight = 0f;
        _gridViewport.style.minWidth = 0f;
        // The catalog uses the same cool treatment at a middle strength: more dimensional than
        // secondary filters, quieter than the control deck.
        ApplyCollectionFilterCardTreatment(_gridViewport, Colors.CollectionFilterCardGridPalette);
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
        _gridDragScroller = new ScrollViewDragScroller(_gridScrollView);
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
        _emptyLabel.pickingMode = PickingMode.Ignore;
        _emptyLabel.style.position = Position.Absolute;
        _emptyLabel.style.left = 0f;
        _emptyLabel.style.right = 0f;
        _emptyLabel.style.top = 0f;
        _emptyLabel.style.bottom = 0f;
        _emptyLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
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
