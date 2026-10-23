#nullable enable
using BazaarPlusPlus.Core.Ui;

namespace BazaarPlusPlus.Game.LiveBuildPanel.Ui;

/// <summary>
/// The Live Build panel's geometry on a landscape touch screen, in canvas units, top-down from
/// the canvas's top-left corner. A full sheet inside the safe area holds two columns. The left one
/// keeps the desktop's four peer boards: the recommended final build pinned at the top, its title
/// band a full touch target that carries the build pager, and the shop, stash, and current board
/// in a drag-scrolled list under it (row-local geometry: <see cref="ScrollRowTitle"/> and
/// <see cref="ScrollRowBoard"/>, from the list content's top). The right one is the overview: the
/// title and close button, the manual refresh, the corpus status (tapped to show its details,
/// which the desktop shows on hover), the hero, the match metrics or guidance, and the tap hint.
/// Every target is at least <see cref="TouchMetrics.MinTargetDp"/> and every text at least
/// <see cref="TouchMetrics.MinBodySp"/>. Cards keep the game's prefab proportions: ten sockets
/// span the board width, unless two rows would then overflow the height. The desktop's
/// <c>LiveBuildPanelLayout</c> is a separate, fixed-pixel layout and is not derived from this one.
/// </summary>
public sealed class LiveBuildTouchLayout
{
    public const int SocketCount = 10;

    /// <summary>The list under the pinned build: shop, stash, and current board.</summary>
    public const int ScrollRowCount = 3;

    public const int MetricCount = 4;

    /// <summary>A rented card prefab's height, as the Android card rental sets it up.</summary>
    public const float PrefabHeight = 484f;

    /// <summary>One socket of the prefab: a small item's width at <see cref="PrefabHeight"/>.</summary>
    public const float PrefabSocketWidth = 252f;

    /// <summary>The card frame stands this much taller than its socket.</summary>
    public const float FrameHeightOverSocket = 1.037f;

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
        var heightScale = (body.Height - fixedHeight) / 2f / (PrefabHeight * FrameHeightOverSocket);
        var widthScale = columnWidth / (PrefabSocketWidth * SocketCount);
        CardScale = Math.Max(0.01f, Math.Min(widthScale, heightScale));
        SocketWidth = PrefabSocketWidth * CardScale;
        CardHeight = PrefabHeight * FrameHeightOverSocket * CardScale;
        CarpetWidth = SocketWidth * SocketCount;
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
        var pagerButton = metrics.Target(64f);
        Next = new TouchRect(PinnedTitle.Right - pagerButton, PinnedTitle.Top, pagerButton, target);
        Pager = new TouchRect(
            Next.Left - metrics.Dp(88f),
            PinnedTitle.Top,
            metrics.Dp(88f),
            target
        );
        Previous = new TouchRect(Pager.Left - pagerButton, PinnedTitle.Top, pagerButton, target);
        PinnedTitleText = new TouchRect(
            PinnedTitle.Left,
            PinnedTitle.Top,
            Math.Max(0f, Previous.Left - Gap - PinnedTitle.Left),
            target
        );

        var scrollTop = PinnedBoard.Bottom + Gap;
        Scroll = new TouchRect(
            BoardColumn.Left,
            scrollTop,
            columnWidth,
            Math.Max(0f, body.Bottom - scrollTop)
        );
        ScrollRowHeight = RowTitleHeight + CardHeight + TagHeight;
        ScrollContentHeight = ScrollRowHeight * ScrollRowCount + Gap * (ScrollRowCount - 1);

        var side = Sidebar;
        Close = chrome.CloseAt(side);
        Title = chrome.TitleBeside(side, Close);
        Refresh = new TouchRect(side.Left, Close.Bottom + Gap, side.Width, target);
        Corpus = new TouchRect(side.Left, Refresh.Bottom + Gap, side.Width, target);
        Hero = new TouchRect(side.Left, Corpus.Bottom + Gap, side.Width, metrics.Dp(28f));
        Hint = new TouchRect(side.Left, side.Bottom - metrics.Dp(40f), side.Width, metrics.Dp(40f));
        Info = new TouchRect(
            side.Left,
            Hero.Bottom + Gap,
            side.Width,
            Math.Max(0f, Hint.Top - Gap - (Hero.Bottom + Gap))
        );
        MetricColumns = TouchMetrics.ColumnCount(Info.Width, metrics.Dp(110f), Gap, 2);
        var metricRows = (MetricCount + MetricColumns - 1) / MetricColumns;
        MetricCellWidth = (Info.Width - Gap * (MetricColumns - 1)) / MetricColumns;
        MetricCellHeight = Math.Min(
            metrics.Dp(64f),
            (Info.Height - Gap * (metricRows - 1)) / metricRows
        );

        _inspect = chrome.Inspect();
        InspectCardArea = _inspect.CardArea;
        InspectInfo = _inspect.Info;
        InspectClose = _inspect.Close;
        InspectName = _inspect.Name;
        InspectDetail = _inspect.Detail;
    }

    private readonly TouchInspectLayout _inspect;

    public static LiveBuildTouchLayout For(TouchMetrics metrics) => new(metrics);

    public TouchMetrics Metrics { get; }

    public float Gap { get; }

    public float TitleFont { get; }

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

    public float CardScale { get; }

    public float SocketWidth { get; }

    /// <summary>A rented card's framed height on the boards.</summary>
    public float CardHeight { get; }

    /// <summary>Ten sockets' width: no card reaches past it.</summary>
    public float CarpetWidth { get; }

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

    public TouchRect Title { get; }

    public TouchRect Close { get; }

    public TouchRect Refresh { get; }

    public TouchRect Corpus { get; }

    public TouchRect Hero { get; }

    /// <summary>The match metrics, the guidance, or the corpus details, one at a time.</summary>
    public TouchRect Info { get; }

    public TouchRect Hint { get; }

    public int MetricColumns { get; }

    public float MetricCellWidth { get; }

    public float MetricCellHeight { get; }

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
        return _inspect.Scale(
            PrefabSocketWidth * clampedSpan,
            PrefabHeight * FrameHeightOverSocket
        );
    }

    private float RowTop(int row) =>
        Math.Clamp(row, 0, ScrollRowCount - 1) * (ScrollRowHeight + Gap);
}
