#nullable enable
using BazaarPlusPlus.Core.Cards;
using BazaarPlusPlus.Core.Ui;

namespace BazaarPlusPlus.Game.LiveBuildPanel.Ui;

/// <summary>
/// The Live Build panel's geometry on a landscape touch screen, in canvas units, top-down from
/// the canvas's top-left corner. Its pieces are the desktop's four peer boards (the recommended
/// final build, its title band a full touch target that carries the build pager, then the shop,
/// stash, and current board in a drag-scrolled list) and the overview: the title and close
/// button, the manual refresh, the corpus status (tapped to show its details, which the desktop
/// shows on hover), the hero, the match metrics or guidance, and the tap hint. Every target is at
/// least <see cref="TouchMetrics.MinTargetDp"/>, every metric cell at least
/// <see cref="MinMetricCellDp"/> high, every text at least
/// <see cref="TouchMetrics.MinBodySp"/>, and every card socket at least
/// <see cref="TouchMetrics.MinTargetDp"/> wide, since a socket is what a tap picks. Cards keep the
/// game's prefab proportions (<see cref="NativeCardPrefab"/>).
/// <para>
/// Side by side (the default): the boards take the left column, the final build pinned over the
/// list, and the overview the right one; ten sockets span the column unless two rows would then
/// overflow the height. Stacked (<see cref="Stacked"/>), when that arrangement leaves a socket
/// under 48dp or the sidebar too short for the metric cells, as on a dense screen: the final build's title band, with the pager and the close
/// button, stays as a header over the full width, and everything else scrolls under it, the
/// final build's cards first, then the three list rows, then the overview. Stacked, the final
/// build's board and the overview's rects are list-content-local, as the rows' are.
/// </para>
/// The desktop's <c>LiveBuildPanelLayout</c> is a separate, fixed-pixel layout and is not derived
/// from this one.
/// </summary>
public sealed class LiveBuildTouchLayout
{
    public const int SocketCount = 10;

    /// <summary>The list under the pinned build: shop, stash, and current board.</summary>
    public const int ScrollRowCount = 3;

    public const int MetricCount = 4;

    /// <summary>The shortest a match metric's cell gets, one value and its label.</summary>
    public const float MinMetricCellDp = 40f;

    private const float FramedHeight =
        NativeCardPrefab.Height * NativeCardPrefab.FrameHeightOverSocket;

    private readonly float _listTop;

    private LiveBuildTouchLayout(TouchMetrics metrics)
    {
        Metrics = metrics;
        var chrome = TouchChrome.For(metrics);
        var padding = chrome.Padding;
        Gap = chrome.Gap;
        var target = chrome.Target;

        TitleFont = chrome.TitleFont;
        RowTitleFont = chrome.ButtonFont;
        ButtonFont = chrome.ButtonFont;
        BodyFont = chrome.BodyFont;
        ValueFont = metrics.Sp(16f);
        TagFont = chrome.BodyFont;
        NameFont = chrome.NameFont;
        LabelInset = chrome.LabelInset;

        Sheet = chrome.Sheet;
        var body = chrome.Body;

        RowTitleHeight = metrics.Dp(24f);
        PinnedTitleHeight = target;
        TagHeight = metrics.Dp(22f);

        // The sidebar takes about a third of the width, within readable bounds, and never so
        // much that the boards lose half the sheet.
        var sidebarWidth = Math.Min(
            Math.Clamp(body.Width * 0.3f, metrics.Dp(240f), metrics.Dp(360f)),
            body.Width * 0.45f
        );
        var columnWidth = Math.Max(1f, body.Width - sidebarWidth - padding);
        // Two rows stay whole on screen: the pinned build and one row of the list.
        var fixedHeight = PinnedTitleHeight + TagHeight + Gap + RowTitleHeight + TagHeight + Gap;
        var heightScale = (body.Height - fixedHeight) / 2f / FramedHeight;
        var widthScale = columnWidth / (NativeCardPrefab.SocketWidth * SocketCount);
        var sideBySideScale = Math.Max(0.01f, Math.Min(widthScale, heightScale));
        SupportersHeight = Math.Max(metrics.Dp(24f), BodyFont * 1.5f);
        Stacked =
            NativeCardPrefab.SocketWidth * sideBySideScale < metrics.MinTarget - 0.01f
            || !SidebarHoldsTheMetrics(metrics, body.Height, sidebarWidth, target);

        if (!Stacked)
        {
            SetCardScale(sideBySideScale);
            // A height-bound carpet is narrower than its column: the sidebar takes the rest.
            columnWidth = Math.Min(columnWidth, CarpetWidth);
            BoardColumn = new TouchRect(body.Left, body.Top, columnWidth, body.Height);
            Sidebar = new TouchRect(
                BoardColumn.Right + padding,
                body.Top,
                body.Right - BoardColumn.Right - padding,
                body.Height
            );
            PinnedTitle = new TouchRect(BoardColumn.Left, body.Top, columnWidth, PinnedTitleHeight);
            PinnedBoard = new TouchRect(
                BoardColumn.Left,
                PinnedTitle.Bottom,
                columnWidth,
                CardHeight + TagHeight
            );
            Close = chrome.CloseAt(Sidebar);
            Next = PagerFrom(
                PinnedTitle.Right,
                PinnedTitle.Top,
                metrics,
                target,
                out var pager,
                out var previous
            );
            Pager = pager;
            Previous = previous;
            var scrollTop = PinnedBoard.Bottom + Gap;
            Scroll = new TouchRect(
                BoardColumn.Left,
                scrollTop,
                columnWidth,
                Math.Max(0f, body.Bottom - scrollTop)
            );
            _listTop = 0f;
            ScrollRowHeight = RowTitleHeight + CardHeight + TagHeight;
            var listHeight = ScrollRowHeight * ScrollRowCount + Gap * (ScrollRowCount - 1);
            LayOverview(chrome, Sidebar, Close.Bottom + Gap);
            ScrollContentHeight = listHeight;
        }
        else
        {
            BoardColumn = body;
            PinnedTitle = new TouchRect(body.Left, body.Top, body.Width, PinnedTitleHeight);
            Close = chrome.CloseAt(body);
            Next = PagerFrom(
                Close.Left - Gap,
                PinnedTitle.Top,
                metrics,
                target,
                out var pager,
                out var previous
            );
            Pager = pager;
            Previous = previous;
            var scrollTop = PinnedTitle.Bottom + Gap;
            Scroll = new TouchRect(
                body.Left,
                scrollTop,
                body.Width,
                Math.Max(0f, body.Bottom - scrollTop)
            );
            // Ten sockets span the width, and one list row stays whole in the viewport.
            var stackedWidthScale = body.Width / (NativeCardPrefab.SocketWidth * SocketCount);
            var rowScale = Math.Max(0f, Scroll.Height - RowTitleHeight - TagHeight) / FramedHeight;
            SetCardScale(Math.Max(0.01f, Math.Min(stackedWidthScale, rowScale)));
            PinnedBoard = new TouchRect(0f, 0f, body.Width, CardHeight + TagHeight);
            _listTop = PinnedBoard.Bottom + Gap;
            ScrollRowHeight = RowTitleHeight + CardHeight + TagHeight;
            var overviewTop = _listTop + ScrollRowCount * (ScrollRowHeight + Gap);
            var overview = new TouchRect(0f, overviewTop, body.Width, 0f);
            var bottom = LayOverview(chrome, overview, overviewTop);
            Sidebar = new TouchRect(0f, overviewTop, body.Width, bottom - overviewTop);
            ScrollContentHeight = bottom;
        }

        PinnedTitleText = new TouchRect(
            PinnedTitle.Left,
            PinnedTitle.Top,
            Math.Max(0f, Previous.Left - Gap - PinnedTitle.Left),
            target
        );

        _inspect = chrome.Inspect();
        InspectCardArea = _inspect.CardArea;
        InspectInfo = _inspect.Info;
        InspectClose = _inspect.Close;
        InspectName = _inspect.Name;
        InspectDetail = _inspect.Detail;
    }

    private void SetCardScale(float scale)
    {
        CardScale = scale;
        SocketWidth = NativeCardPrefab.SocketWidth * scale;
        CardHeight = FramedHeight * scale;
        CarpetWidth = SocketWidth * SocketCount;
    }

    // The pager ends at `right`: previous, the "n / m" label, next.
    private static TouchRect PagerFrom(
        float right,
        float top,
        TouchMetrics metrics,
        float target,
        out TouchRect pager,
        out TouchRect previous
    )
    {
        var pagerButton = metrics.Target(64f);
        var next = new TouchRect(right - pagerButton, top, pagerButton, target);
        pager = new TouchRect(next.Left - metrics.Dp(88f), top, metrics.Dp(88f), target);
        previous = new TouchRect(pager.Left - pagerButton, top, pagerButton, target);
        return next;
    }

    // The overview's controls down `area` from `top`. Side by side the column ends at the
    // sheet's bottom: the hint and the sponsor button share the last row, the supporter credit is
    // one line over it, and the info area takes what is left. Stacked, each has its own height
    // and the column ends under that row. Returns that bottom.
    private float LayOverview(TouchChrome chrome, TouchRect area, float top)
    {
        var metrics = chrome.Metrics;
        var target = chrome.Target;
        if (!Stacked)
            Title = chrome.TitleBeside(area, Close);
        else
            Title = new TouchRect(area.Left, top, area.Width, target);
        var below = Stacked ? Title.Bottom + Gap : top;
        Refresh = new TouchRect(area.Left, below, area.Width, target);
        Corpus = new TouchRect(area.Left, Refresh.Bottom + Gap, area.Width, target);
        Hero = new TouchRect(area.Left, Corpus.Bottom + Gap, area.Width, metrics.Dp(28f));
        // One line of names, its own line height the gap over it; the row is not a target, the
        // sponsor button beside the hint is.
        var supportersHeight = SupportersHeight;
        var infoTop = Hero.Bottom + Gap;
        var lastRow = default(TouchRect);
        if (!Stacked)
        {
            lastRow = new TouchRect(area.Left, area.Bottom - target, area.Width, target);
            Supporters = new TouchRect(
                area.Left,
                lastRow.Top - supportersHeight,
                area.Width,
                supportersHeight
            );
            Info = new TouchRect(
                area.Left,
                infoTop,
                area.Width,
                Math.Max(0f, Supporters.Top - infoTop)
            );
            MetricColumns = TouchMetrics.ColumnCount(Info.Width, metrics.Dp(110f), Gap, 2);
        }
        else
        {
            MetricColumns = TouchMetrics.ColumnCount(area.Width, metrics.Dp(110f), Gap, 4);
        }
        var metricRows = (MetricCount + MetricColumns - 1) / MetricColumns;
        if (Stacked)
        {
            // Room for the metric cells, and for the guidance or corpus details shown there instead.
            var infoHeight = Math.Max(
                metricRows * metrics.Dp(64f) + Gap * (metricRows - 1),
                metrics.Dp(120f)
            );
            Info = new TouchRect(area.Left, infoTop, area.Width, infoHeight);
            Supporters = new TouchRect(area.Left, Info.Bottom + Gap, area.Width, supportersHeight);
            lastRow = new TouchRect(area.Left, Supporters.Bottom, area.Width, target);
        }
        var sponsorWidth = Math.Max(
            metrics.MinTarget,
            Math.Min(metrics.Dp(96f), lastRow.Width / 3f)
        );
        Sponsor = new TouchRect(lastRow.Right - sponsorWidth, lastRow.Top, sponsorWidth, target);
        Hint = new TouchRect(
            lastRow.Left,
            lastRow.Top,
            Math.Max(0f, Sponsor.Left - Gap - lastRow.Left),
            target
        );
        MetricCellWidth = (Info.Width - Gap * (MetricColumns - 1)) / MetricColumns;
        MetricCellHeight = Math.Min(
            metrics.Dp(64f),
            (Info.Height - Gap * (metricRows - 1)) / metricRows
        );
        return Hint.Bottom;
    }

    // Side by side, the sidebar's fixed rows (LayOverview) leave the info area room for every
    // metric cell at its floor.
    private bool SidebarHoldsTheMetrics(
        TouchMetrics metrics,
        float height,
        float width,
        float target
    )
    {
        var columns = TouchMetrics.ColumnCount(width, metrics.Dp(110f), Gap, 2);
        var rows = (MetricCount + columns - 1) / columns;
        var fixedRows = target * 4f + metrics.Dp(28f) + Gap * 4f + SupportersHeight;
        var info = rows * metrics.Dp(MinMetricCellDp) + Gap * (rows - 1);
        return height - fixedRows >= info - 0.01f;
    }

    private readonly TouchInspectLayout _inspect;

    public static LiveBuildTouchLayout For(TouchMetrics metrics) => new(metrics);

    /// <summary>
    /// The dense arrangement: the final build's board and the overview scroll with the list, and
    /// their rects are list-content-local.
    /// </summary>
    public bool Stacked { get; }

    public TouchMetrics Metrics { get; }

    public float Gap { get; }

    public float TitleFont { get; }

    /// <summary>A button's label padding (<see cref="TouchChrome.LabelInset"/>).</summary>
    public float LabelInset { get; }

    public float RowTitleFont { get; }

    public float ButtonFont { get; }

    public float BodyFont { get; }

    public float ValueFont { get; }

    public float TagFont { get; }

    public float NameFont { get; }

    /// <summary>The whole panel: the safe area less a margin.</summary>
    public TouchRect Sheet { get; }

    public TouchRect BoardColumn { get; }

    public TouchRect Sidebar { get; }

    public float RowTitleHeight { get; }

    public float PinnedTitleHeight { get; }

    public float TagHeight { get; }

    /// <summary>The supporter credit's one line.</summary>
    public float SupportersHeight { get; }

    public float CardScale { get; private set; }

    public float SocketWidth { get; private set; }

    /// <summary>A rented card's framed height on the boards.</summary>
    public float CardHeight { get; private set; }

    /// <summary>Ten sockets' width: no card reaches past it.</summary>
    public float CarpetWidth { get; private set; }

    /// <summary>The pinned build's title band, a full touch target.</summary>
    public TouchRect PinnedTitle { get; }

    public TouchRect PinnedTitleText { get; }

    public TouchRect Previous { get; }

    public TouchRect Pager { get; }

    public TouchRect Next { get; }

    /// <summary>The pinned build's cards and the tag band under them.</summary>
    public TouchRect PinnedBoard { get; }

    /// <summary>The drag-scrolled list's viewport.</summary>
    public TouchRect Scroll { get; }

    public float ScrollRowHeight { get; }

    public float ScrollContentHeight { get; }

    public TouchRect Title { get; private set; }

    public TouchRect Close { get; }

    public TouchRect Refresh { get; private set; }

    public TouchRect Corpus { get; private set; }

    public TouchRect Hero { get; private set; }

    /// <summary>The match metrics, the guidance, or the corpus details, one at a time.</summary>
    public TouchRect Info { get; private set; }

    public TouchRect Hint { get; private set; }

    /// <summary>
    /// The supporter credit, the desktop panel's attribution row: one line of names over the
    /// hint, with <see cref="Sponsor"/> at the hint's right.
    /// </summary>
    public TouchRect Supporters { get; private set; }

    /// <summary>The button that opens the sponsor page, as the desktop row's action does.</summary>
    public TouchRect Sponsor { get; private set; }

    public int MetricColumns { get; private set; }

    public float MetricCellWidth { get; private set; }

    public float MetricCellHeight { get; private set; }

    public TouchRect InspectCardArea { get; }

    public TouchRect InspectInfo { get; }

    public TouchRect InspectName { get; }

    public TouchRect InspectClose { get; }

    public TouchRect InspectDetail { get; }

    /// <summary>A list row's title band, from the list content's top-left corner.</summary>
    public TouchRect ScrollRowTitle(int row) =>
        new(0f, RowTop(row), BoardColumn.Width, RowTitleHeight);

    /// <summary>A list row's cards and tag band, from the list content's top-left corner.</summary>
    public TouchRect ScrollRowBoard(int row) =>
        new(0f, RowTop(row) + RowTitleHeight, BoardColumn.Width, CardHeight + TagHeight);

    /// <summary>One metric's cell, row-major, in canvas units.</summary>
    public TouchRect MetricCell(int index)
    {
        var column = index % MetricColumns;
        var row = index / MetricColumns;
        return new TouchRect(
            Info.Left + column * (MetricCellWidth + Gap),
            Info.Top + row * (MetricCellHeight + Gap),
            MetricCellWidth,
            MetricCellHeight
        );
    }

    /// <summary>
    /// A card's footprint in its board, from the board's top-left corner: <paramref name="span"/>
    /// sockets from <paramref name="socket"/>, the tag band left free under it.
    /// </summary>
    public TouchRect CardRect(int socket, int span)
    {
        var clampedSpan = Math.Clamp(span, 1, SocketCount);
        var clampedSocket = Math.Clamp(socket, 0, SocketCount - clampedSpan);
        return new TouchRect(
            SocketWidth * clampedSocket,
            0f,
            SocketWidth * clampedSpan,
            CardHeight
        );
    }

    /// <summary>A card's tag under its footprint, from the board's top-left corner.</summary>
    public TouchRect TagRect(int socket, int span)
    {
        var card = CardRect(socket, span);
        var width = Math.Min(card.Width, Math.Max(card.Width * 0.8f, Metrics.Dp(56f)));
        return new TouchRect(card.CenterX - width / 2f, card.Bottom, width, TagHeight);
    }

    /// <summary>The enlarged card's scale on the inspect sheet for a card <paramref name="span"/> sockets wide.</summary>
    public float InspectScale(int span)
    {
        var clampedSpan = Math.Clamp(span, 1, 3);
        return _inspect.Scale(NativeCardPrefab.SocketWidth * clampedSpan, FramedHeight);
    }

    private float RowTop(int row) =>
        _listTop + Math.Clamp(row, 0, ScrollRowCount - 1) * (ScrollRowHeight + Gap);
}
