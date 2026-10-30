#nullable enable
using BazaarPlusPlus.Core.Cards;
using BazaarPlusPlus.Core.Ui;

namespace BazaarPlusPlus.Game.LiveBuildPanel.Ui;

/// <summary>
/// Touch geometry for a pinned recommendation and one selectable live board. Ordinary landscape
/// screens keep both boards fixed; dense screens scroll the same two boards and their overview
/// so sockets still meet the 48dp floor. The overview starts with a supporter action beside Close.
/// </summary>
public sealed class LiveBuildTouchLayout
{
    public const int SocketCount = 10;

    /// <summary>One board slot, switched between current board, stash and shop.</summary>
    public const int ScrollRowCount = 1;

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

        RowTitleHeight = 0f;
        PinnedTitleHeight = target;
        TagHeight = metrics.Dp(22f);

        // A compact utility column leaves the remaining width to the two boards.
        var sidebarWidth = Math.Min(metrics.Dp(240f), body.Width * 0.45f);
        var columnWidth = Math.Max(1f, body.Width - sidebarWidth - padding);
        // Two rows stay whole on screen: the pinned build and one row of the list.
        var fixedHeight = PinnedTitleHeight + TagHeight * 2f + Gap * 6f;
        var heightScale = (body.Height - fixedHeight) / 2f / FramedHeight;
        var widthScale = (columnWidth - Gap * 2f) / (NativeCardPrefab.SocketWidth * SocketCount);
        var sideBySideScale = Math.Max(0.01f, Math.Min(widthScale, heightScale));
        var boardColumnHeight =
            PinnedTitleHeight + 2f * (FramedHeight * sideBySideScale + TagHeight + Gap * 2f) + Gap;
        // A screen squarer than 16:10 holds the side-by-side boards by width and leaves the
        // lower half empty; stacked, they take the full width and the overview fills the rest.
        // That arrangement is taken whenever it fits without scrolling and its cards are larger.
        var roomyScale = StackedScaleWithoutScrolling(metrics, body, target);
        Roomy = roomyScale > sideBySideScale * RoomyGain;
        Stacked =
            Roomy
            || NativeCardPrefab.SocketWidth * sideBySideScale < metrics.MinTarget - 0.01f
            || !SidebarHoldsTheMetrics(metrics, boardColumnHeight, sidebarWidth, target);

        if (!Stacked)
        {
            SetCardScale(sideBySideScale);
            // Keep the utility column compact; extra width belongs to the boards.
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
                CardHeight + TagHeight + Gap * 2f
            );
            Close = chrome.CloseAt(Sidebar);
            var pagerTop = Close.Bottom + Gap + metrics.Dp(96f) + Gap + target + Gap;
            // Align the final action to the rendered lower board frame, not the scroll viewport.
            var actionBottom = body.Top + boardColumnHeight;
            var actionHeight = (actionBottom - pagerTop - Gap) / 2f;
            Next = PagerFrom(
                Sidebar.Right,
                pagerTop,
                metrics,
                actionHeight,
                out var pager,
                out var previous,
                Sidebar.Width
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
            ScrollRowHeight = RowTitleHeight + CardHeight + TagHeight + Gap * 2f;
            LayOverview(chrome, Sidebar, Close.Bottom + Gap);
            ScrollContentHeight = Scroll.Height;
        }
        else
        {
            RowTitleHeight = target;
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
            var stackedWidthScale =
                (body.Width - Gap * 2f) / (NativeCardPrefab.SocketWidth * SocketCount);
            var rowScale =
                Math.Max(0f, Scroll.Height - RowTitleHeight - TagHeight - Gap * 2f) / FramedHeight;
            SetCardScale(
                Math.Max(0.01f, Roomy ? roomyScale : Math.Min(stackedWidthScale, rowScale))
            );
            PinnedBoard = new TouchRect(0f, 0f, body.Width, CardHeight + TagHeight + Gap * 2f);
            _listTop = PinnedBoard.Bottom + Gap;
            ScrollRowHeight = RowTitleHeight + CardHeight + TagHeight + Gap * 2f;
            var overviewTop = _listTop + ScrollRowCount * (ScrollRowHeight + Gap);
            var overview = new TouchRect(0f, overviewTop, body.Width, 0f);
            var bottom = LayOverview(chrome, overview, overviewTop);
            Sidebar = new TouchRect(0f, overviewTop, body.Width, bottom - overviewTop);
            ScrollContentHeight = bottom;
        }

        BoardSelector = Stacked
            ? new TouchRect(0f, _listTop, metrics.Target(160f), target)
            : new TouchRect(Sidebar.Left, Next.Bottom + Gap, Sidebar.Width, Next.Height);

        PinnedTitleText = new TouchRect(
            PinnedTitle.Left,
            PinnedTitle.Top,
            Stacked ? Math.Max(0f, Previous.Left - Gap - PinnedTitle.Left) : PinnedTitle.Width,
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
        out TouchRect previous,
        float width = 0f
    )
    {
        var pagerButton = width > 0f ? (width - metrics.Dp(88f)) / 2f : metrics.Target(64f);
        var next = new TouchRect(right - pagerButton, top, pagerButton, target);
        pager = new TouchRect(next.Left - metrics.Dp(88f), top, metrics.Dp(88f), target);
        previous = new TouchRect(pager.Left - pagerButton, top, pagerButton, target);
        return next;
    }

    private float LayOverview(TouchChrome chrome, TouchRect area, float top)
    {
        var metrics = chrome.Metrics;
        var target = chrome.Target;
        Sponsor = !Stacked
            ? chrome.TitleBeside(area, Close)
            : new TouchRect(area.Left, top, area.Width, target);
        MetricColumns = TouchMetrics.ColumnCount(
            area.Width,
            metrics.Dp(110f),
            Gap,
            Stacked ? 4 : 2
        );
        var metricRows = (MetricCount + MetricColumns - 1) / MetricColumns;
        if (Stacked)
        {
            LayLibraryAction(area, Sponsor.Bottom + Gap, target);
            Hero = new TouchRect(area.Left, Corpus.Bottom + Gap, area.Width, metrics.Dp(28f));
            Info = new TouchRect(
                area.Left,
                Hero.Bottom + Gap,
                area.Width,
                Math.Max(metricRows * metrics.Dp(64f) + Gap * (metricRows - 1), metrics.Dp(120f))
            );
        }
        else
        {
            Hero = new TouchRect(area.Left, top, area.Width, 0f);
            Info = new TouchRect(area.Left, top, area.Width, metrics.Dp(96f));
            LayLibraryAction(area, Info.Bottom + Gap, target);
        }
        MetricCellWidth = (Info.Width - Gap * (MetricColumns - 1)) / MetricColumns;
        MetricCellHeight = Math.Min(
            metrics.Dp(64f),
            (Info.Height - Gap * (metricRows - 1)) / metricRows
        );
        return Info.Bottom;
    }

    private void LayLibraryAction(TouchRect area, float top, float target)
    {
        var actionWidth = Metrics.Target(88f);
        Refresh = new TouchRect(area.Right - actionWidth, top, actionWidth, target);
        Corpus = new TouchRect(area.Left, top, area.Width - actionWidth - Gap, target);
    }

    /// <summary>How much larger stacked cards must be before a screen with room stacks.</summary>
    private const float RoomyGain = 1.15f;

    // Stacked, the card scale at which the header, both boards, the list row's title and the
    // overview (as LayOverview stacks it) fill the body without scrolling, ten sockets at most
    // spanning its width.
    private float StackedScaleWithoutScrolling(TouchMetrics metrics, TouchRect body, float target)
    {
        var columns = TouchMetrics.ColumnCount(body.Width, metrics.Dp(110f), Gap, 4);
        var rows = (MetricCount + columns - 1) / columns;
        var info = Math.Max(rows * metrics.Dp(64f) + Gap * (rows - 1), metrics.Dp(120f));
        var overview = target + Gap + target + Gap + metrics.Dp(28f) + Gap + info;
        var fixedHeight =
            PinnedTitleHeight
            + Gap
            + (TagHeight + Gap * 2f + Gap)
            + (target + TagHeight + Gap * 2f + Gap)
            + overview;
        var widthScale = (body.Width - Gap * 2f) / (NativeCardPrefab.SocketWidth * SocketCount);
        var heightScale = (body.Height - fixedHeight) / 2f / FramedHeight;
        return Math.Min(widthScale, heightScale);
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
        var info = rows * metrics.Dp(44f) + Gap * (rows - 1);
        return height >= target * 4f + Gap * 4f + info - 0.01f;
    }

    private readonly TouchInspectLayout _inspect;

    public static LiveBuildTouchLayout For(TouchMetrics metrics) => new(metrics);

    /// <summary>
    /// The dense arrangement: the final build's board and the overview scroll with the list, and
    /// their rects are list-content-local.
    /// </summary>
    public bool Stacked { get; }

    /// <summary>
    /// Stacked because the screen has room rather than density: everything fits the viewport, so
    /// nothing scrolls.
    /// </summary>
    public bool Roomy { get; }

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

    public TouchRect Close { get; }

    /// <summary>Lower-board switch: sidebar-local to the sheet, or list-local when stacked.</summary>
    public TouchRect BoardSelector { get; }

    public TouchRect Refresh { get; private set; }

    public TouchRect Corpus { get; private set; }

    public TouchRect Hero { get; private set; }

    /// <summary>The match metrics, the guidance, or the corpus details, one at a time.</summary>
    public TouchRect Info { get; private set; }

    /// <summary>One supporter credit and sponsor link in the overview header.</summary>
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
        new(0f, RowTop(row) + RowTitleHeight, BoardColumn.Width, CardHeight + TagHeight + Gap * 2f);

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
