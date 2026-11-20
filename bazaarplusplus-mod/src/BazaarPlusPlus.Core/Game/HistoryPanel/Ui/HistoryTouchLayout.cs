#nullable enable
using BazaarPlusPlus.Core.Cards;
using BazaarPlusPlus.Core.Ui;

namespace BazaarPlusPlus.Game.HistoryPanel.Ui;

/// <summary>Canvas and scroll-content coordinates use a top-left origin.
/// Use a single board when two would push each socket below the minimum touch target.</summary>
public sealed class HistoryTouchLayout
{
    public const int SocketCount = 10;

    public const int SkillsPerRow = 10;

    public const float DetailSocketDp = 52f;

    public const int PagerButtonCount = 3;

    public const float ArchiveMaxDp = 360f;

    public const float RowDp = 52f;

    public const float NarrowRowDp = 64f;

    /// <summary>Below this width, use a third row line to fit the badges and text.</summary>
    public const float TwoLineRowMinDp = 280f;

    public const float PageLabelMinDp = 120f;

    private HistoryTouchLayout(TouchMetrics metrics)
    {
        Metrics = metrics;
        var chrome = TouchChrome.For(metrics);
        var padding = chrome.Padding;
        var small = metrics.Dp(4f);
        Gap = chrome.Gap;
        var target = chrome.Target;
        _chrome = chrome;
        _padding = padding;
        _target = target;

        TitleFont = chrome.TitleFont;
        ButtonFont = chrome.ButtonFont;
        RowTitleFont = chrome.ButtonFont;
        BodyFont = chrome.BodyFont;
        NameFont = chrome.NameFont;

        Sheet = chrome.Sheet;
        var body = chrome.Body;

        Close = chrome.Close;
        More = new TouchRect(
            Close.Left - Gap - chrome.CloseWidth,
            body.Top,
            chrome.CloseWidth,
            target
        );
        StatusHeight = metrics.Dp(18f);
        Status = new TouchRect(body.Left, body.Bottom - StatusHeight, body.Width, StatusHeight);
        var top = Close.Bottom + Gap;
        var bottom = Status.Top - small;
        var columnHeight = Math.Max(0f, bottom - top);
        var railWidth = metrics.Target(64f);
        BoardTitleHeight = metrics.Dp(18f);
        var boardGap = metrics.Dp(2f);
        var nativeHeight = NativeCardPrefab.Height * NativeCardPrefab.FrameHeightOverSocket;
        var tallScale =
            (columnHeight - target - Gap - 2f * BoardTitleHeight - boardGap) / (2f * nativeHeight);
        // Reserve room for the action row and give excess board space to the archive on short, wide screens.
        var actionsWidth =
            2f * metrics.Target(64f) + metrics.Target(104f) + metrics.Target(120f) + 4f * Gap;
        var detailNeed = Math.Max(
            SocketCount
                * Math.Min(metrics.Dp(DetailSocketDp), tallScale * NativeCardPrefab.SocketWidth),
            actionsWidth
        );
        var archiveWidth = Math.Clamp(
            body.Width - railWidth - 2f * Gap - detailNeed,
            metrics.Dp(200f),
            metrics.Dp(ArchiveMaxDp)
        );
        var detailWidth = body.Width - archiveWidth - railWidth - 2f * Gap;
        var wideScale = Math.Min(
            detailWidth / (NativeCardPrefab.SocketWidth * SocketCount),
            tallScale
        );
        Compact = wideScale * NativeCardPrefab.SocketWidth < metrics.MinTarget;
        if (Compact)
            archiveWidth = body.Width;
        Archive = new TouchRect(
            body.Left,
            top,
            archiveWidth,
            Compact ? columnHeight : body.Bottom - top
        );
        // Give up tab width and then filter width so the supporter action keeps its minimum touch target.
        var tabsWidth = Compact
            ? Math.Clamp(
                More.Left - body.Left - metrics.Dp(124f + 120f) - 3f * Gap - 2f * small,
                3f * metrics.Target(64f),
                metrics.Dp(288f)
            )
            : Archive.Width - 2f * small;
        var filterWidth = Math.Clamp(
            More.Left - body.Left - tabsWidth - metrics.MinTarget - 3f * Gap - 2f * small,
            metrics.Target(80f),
            metrics.Dp(124f)
        );
        TabsTray = new TouchRect(body.Left + small, body.Top, tabsWidth, target);
        RunsTab = new TouchRect(TabsTray.Left, TabsTray.Top, TabsTray.Width / 3f, target);
        GhostTab = new TouchRect(RunsTab.Right, TabsTray.Top, RunsTab.Width, target);
        FavoritesTab = new TouchRect(GhostTab.Right, TabsTray.Top, RunsTab.Width, target);
        ArchiveFilter = new TouchRect(TabsTray.Right + small + Gap, body.Top, filterWidth, target);
        var supportWidth = Math.Min(metrics.Dp(340f), More.Left - Gap - ArchiveFilter.Right - Gap);
        Sponsor = new TouchRect(More.Left - Gap - supportWidth, body.Top, supportWidth, target);
        ArchivePager = new TouchRect(
            Archive.Left + small,
            Archive.Bottom - target,
            Archive.Width - 2f * small,
            target
        );
        var arrowWidth = metrics.Target(48f);
        var latestWidth = metrics.Target(64f);
        var inlineLabel = ArchivePager.Width - 2f * arrowWidth - latestWidth - 3f * Gap;
        PageLabelInline = inlineLabel >= metrics.Dp(PageLabelMinDp);
        if (PageLabelInline)
        {
            _pagerButtons =
            [
                new(ArchivePager.Left, ArchivePager.Top, arrowWidth, target),
                new(ArchivePager.Right - latestWidth, ArchivePager.Top, latestWidth, target),
                new(
                    ArchivePager.Right - latestWidth - Gap - arrowWidth,
                    ArchivePager.Top,
                    arrowWidth,
                    target
                ),
            ];
            PageLabel = new TouchRect(
                _pagerButtons[0].Right + Gap,
                ArchivePager.Top,
                inlineLabel,
                target
            );
        }
        else
        {
            var width = (ArchivePager.Width - Gap * (PagerButtonCount - 1)) / PagerButtonCount;
            _pagerButtons =
            [
                .. Enumerable
                    .Range(0, PagerButtonCount)
                    .Select(index => new TouchRect(
                        ArchivePager.Left + index * (width + Gap),
                        ArchivePager.Top,
                        width,
                        target
                    )),
            ];
            PageLabel = new TouchRect(
                ArchivePager.Left,
                ArchivePager.Top - small - metrics.Dp(22f),
                ArchivePager.Width,
                metrics.Dp(22f)
            );
        }
        var listBottom = PageLabelInline ? ArchivePager.Top : PageLabel.Top;
        ArchiveList = new TouchRect(
            Archive.Left + small,
            Archive.Top + small,
            Archive.Width - 2f * small,
            Math.Max(0f, listBottom - 2f * small - Archive.Top)
        );
        RowTwoLine = Archive.Width >= metrics.Dp(TwoLineRowMinDp);
        RunRowHeight = metrics.Dp(RowTwoLine ? RowDp : NarrowRowDp);
        BattleRowHeight = RunRowHeight;

        Detail = Compact
            ? new TouchRect(body.Left, body.Top, body.Width, bottom - body.Top)
            : new TouchRect(
                Archive.Right + Gap + railWidth + Gap,
                top,
                detailWidth,
                Archive.Height
            );
        var navigationTop = Detail.Top;
        var navigationRight = Compact ? Close.Left - Gap : Detail.Right;
        Back = new TouchRect(Detail.Left, navigationTop, metrics.Target(72f), target);
        PlayerToggle = new TouchRect(
            navigationRight - metrics.Target(56f),
            navigationTop,
            metrics.Target(56f),
            target
        );
        OpponentToggle = new TouchRect(
            PlayerToggle.Left - small - PlayerToggle.Width,
            navigationTop,
            PlayerToggle.Width,
            target
        );
        Rail = Compact
            ? new TouchRect(
                Back.Right + Gap,
                navigationTop,
                OpponentToggle.Left - Gap - Back.Right - Gap,
                target
            )
            : new TouchRect(Archive.Right + Gap, top, railWidth, columnHeight - target - small);
        RailNewer = new TouchRect(Rail.Left, Rail.Top, Compact ? target : railWidth, target);
        RailOlder = Compact
            ? new TouchRect(Rail.Right - target, Rail.Top, target, target)
            : new TouchRect(Rail.Left, Rail.Bottom - target, railWidth, target);
        RailList = Compact
            ? new TouchRect(
                RailNewer.Right + small,
                navigationTop,
                RailOlder.Left - small - RailNewer.Right - small,
                target
            )
            : new TouchRect(
                Rail.Left,
                RailNewer.Bottom + small,
                railWidth,
                RailOlder.Top - small - RailNewer.Bottom - small
            );
        RailListFull = Rail;
        ChipHeight = metrics.Dp(72f);
        ChipWidth = metrics.Target(64f);
        Footer = new TouchRect(Detail.Left, Detail.Bottom - target, Detail.Width, target);
        Delete = new TouchRect(Footer.Left, Footer.Top, metrics.Target(64f), target);
        Replay = new TouchRect(
            Footer.Right - metrics.Target(120f),
            Footer.Top,
            metrics.Target(120f),
            target
        );
        Skills = new TouchRect(
            Replay.Left - Gap - metrics.Target(64f),
            Footer.Top,
            metrics.Target(64f),
            target
        );
        Favorite = new TouchRect(
            Skills.Left - Gap - metrics.Target(104f),
            Footer.Top,
            metrics.Target(104f),
            target
        );
        // Use only the leftover height for status so it cannot shrink the boards below their chosen scale.
        if (!Compact)
            Status = new TouchRect(
                Detail.Left,
                Footer.Top - small - StatusHeight,
                Detail.Width,
                StatusHeight
            );
        var boardTop = Compact ? Back.Bottom + small : Detail.Top;
        var boardsBottom = (Compact ? Footer.Top : Status.Top) - Gap;
        EmptyDetail = new TouchRect(
            Detail.Left,
            Detail.Top,
            Detail.Width,
            Math.Max(0f, boardsBottom - Detail.Top)
        );
        var boardsHeight = Math.Max(0f, boardsBottom - boardTop);
        var heightScale = Compact
            ? (boardsHeight - BoardTitleHeight) / nativeHeight
            : (boardsHeight - 2f * BoardTitleHeight - boardGap) / (2f * nativeHeight);
        var widthScale = Detail.Width / (NativeCardPrefab.SocketWidth * SocketCount);
        CardScale = Math.Max(.01f, Math.Min(widthScale, heightScale));
        SocketWidth = NativeCardPrefab.SocketWidth * CardScale;
        CardHeight = nativeHeight * CardScale;
        CarpetWidth = SocketWidth * SocketCount;
        var carpetLeft = Detail.Left + (Detail.Width - CarpetWidth) / 2f;
        // Keep titles outside the two boards so the boards can face each other across a small gap.
        UpperBoardTitle = new TouchRect(carpetLeft, boardTop, CarpetWidth, BoardTitleHeight);
        UpperBoard = new TouchRect(carpetLeft, UpperBoardTitle.Bottom, CarpetWidth, CardHeight);
        LowerBoard = Compact
            ? UpperBoard
            : new TouchRect(carpetLeft, UpperBoard.Bottom + boardGap, CarpetWidth, CardHeight);
        LowerBoardTitle = Compact
            ? UpperBoardTitle
            : new TouchRect(carpetLeft, LowerBoard.Bottom, CarpetWidth, BoardTitleHeight);

        _inspect = chrome.Inspect();
        InspectCardArea = _inspect.CardArea;
        InspectInfo = _inspect.Info;
        InspectClose = _inspect.Close;
        InspectName = _inspect.Name;
        InspectDetail = _inspect.Detail;

        var rowInset = SkillRowInset(metrics);
        SkillSide = Math.Max(
            1f,
            Math.Min(
                metrics.Dp(72f),
                Math.Min(
                    (Sheet.Width - 2f * padding - 2f * rowInset - Gap * (SkillsPerRow - 1))
                        / SkillsPerRow,
                    (Sheet.Height - 2f * padding - target - Gap - SkillsHintHeight(metrics)) / 2f
                        - BoardTitleHeight
                        - 2f * rowInset
                        - Gap
                )
            )
        );

        var dialogWidth = Math.Min(metrics.Dp(600f), Sheet.Width);
        Dialog = new TouchRect(
            Sheet.CenterX - dialogWidth / 2f,
            Sheet.Top,
            dialogWidth,
            Sheet.Height
        );
        var dialogBody = Dialog.Inset(padding);
        DialogClose = chrome.CloseAt(dialogBody);
        DialogTitle = chrome.TitleBeside(dialogBody, DialogClose);
        MoreScroll = new TouchRect(
            dialogBody.Left,
            DialogClose.Bottom + Gap,
            dialogBody.Width,
            Math.Max(0f, dialogBody.Bottom - DialogClose.Bottom - Gap)
        );
        var w = MoreScroll.Width;
        var line2 = metrics.Dp(36f);
        var actionWidth = Math.Min(metrics.Target(160f), w * 0.4f);
        MoreHealth = new TouchRect(w - actionWidth, 0f, actionWidth, target);
        MoreDatabase = new TouchRect(0f, 0f, Math.Max(0f, MoreHealth.Left - Gap), target);
        MoreStatus = new TouchRect(0f, MoreHealth.Bottom + small, w, line2);
        AccountToggle = new TouchRect(
            w - actionWidth,
            MoreStatus.Bottom + Gap,
            actionWidth,
            target
        );
        AccountTitle = new TouchRect(
            0f,
            AccountToggle.Top,
            Math.Max(0f, AccountToggle.Left - Gap),
            target
        );
        AccountStatus = new TouchRect(0f, AccountToggle.Bottom + small, w, line2);
        AccountPersistence = new TouchRect(0f, AccountStatus.Bottom + small, w, line2);
        AccountFormTop = AccountPersistence.Bottom + Gap;
        AccountWhy = new TouchRect(0f, AccountFormTop, w, metrics.Dp(72f));
        AccountSubmit = new TouchRect(
            w - actionWidth,
            AccountWhy.Bottom + small,
            actionWidth,
            target
        );
        AccountInput = new TouchRect(
            0f,
            AccountSubmit.Top,
            Math.Max(0f, AccountSubmit.Left - Gap),
            target
        );
        AccountBanner = new TouchRect(0f, AccountSubmit.Bottom + small, w, line2);
        var linkedWidth = Math.Min(metrics.Target(220f), w);
        AccountLinked = new TouchRect(
            w - linkedWidth,
            AccountBanner.Bottom + small,
            linkedWidth,
            target
        );
        MoreContentHeight = AccountLinked.Bottom;

        var buttonWidth = metrics.Target(112f);
        NoteSave = new TouchRect(
            dialogBody.Right - buttonWidth,
            dialogBody.Bottom - target,
            buttonWidth,
            target
        );
        NoteCancel = new TouchRect(
            NoteSave.Left - Gap - buttonWidth,
            NoteSave.Top,
            buttonWidth,
            target
        );
        NoteCount = new TouchRect(
            dialogBody.Left,
            NoteSave.Top,
            Math.Max(0f, NoteCancel.Left - Gap - dialogBody.Left),
            target
        );
        NoteInput = new TouchRect(
            dialogBody.Left,
            DialogClose.Bottom + Gap,
            dialogBody.Width,
            Math.Max(0f, NoteSave.Top - Gap - DialogClose.Bottom - Gap)
        );
    }

    private readonly TouchInspectLayout _inspect;
    private readonly TouchChrome _chrome;
    private readonly float _padding;
    private readonly float _target;
    private readonly TouchRect[] _pagerButtons;

    public static HistoryTouchLayout For(TouchMetrics metrics) => new(metrics);

    public TouchMetrics Metrics { get; }

    public float Gap { get; }

    public float TitleFont { get; }

    public float ButtonFont { get; }

    public float RowTitleFont { get; }

    public float BodyFont { get; }

    public float NameFont { get; }

    public TouchRect Sheet { get; }

    public TouchRect TabsTray { get; }

    public bool Compact { get; }

    public TouchRect ArchiveFilter { get; }
    public TouchRect Back { get; }
    public TouchRect OpponentToggle { get; }
    public TouchRect PlayerToggle { get; }
    public float ChipWidth { get; }

    public TouchRect RunsTab { get; }

    public TouchRect GhostTab { get; }

    public TouchRect FavoritesTab { get; }

    public TouchRect Sponsor { get; }

    public TouchRect More { get; }

    public TouchRect Close { get; }

    public float StatusHeight { get; }

    public TouchRect Status { get; private init; }

    public TouchRect Archive { get; }

    public TouchRect ArchiveList { get; }

    public TouchRect PageLabel { get; }

    public bool PageLabelInline { get; }

    public TouchRect ArchivePager { get; }

    public bool RowTwoLine { get; }

    public float RunRowHeight { get; }

    public float BattleRowHeight { get; }

    public TouchRect Rail { get; }

    public TouchRect RailNewer { get; }

    public TouchRect RailList { get; }

    public TouchRect RailOlder { get; }

    public TouchRect RailListFull { get; }

    public float ChipHeight { get; }

    public TouchRect Detail { get; }

    public TouchRect EmptyDetail { get; }

    public TouchRect Footer { get; }

    public TouchRect Delete { get; }

    public TouchRect Favorite { get; }

    public TouchRect Skills { get; }

    public TouchRect Replay { get; }

    public float BoardTitleHeight { get; }

    public float CardScale { get; }

    public float SocketWidth { get; }

    public float CardHeight { get; }

    public float CarpetWidth { get; }

    public TouchRect UpperBoardTitle { get; }

    public TouchRect UpperBoard { get; }

    public TouchRect LowerBoardTitle { get; }

    public TouchRect LowerBoard { get; }

    public TouchRect InspectCardArea { get; }

    public TouchRect InspectInfo { get; }

    public TouchRect InspectName { get; }

    public TouchRect InspectClose { get; }

    public TouchRect InspectDetail { get; }

    public float SkillSide { get; }

    public TouchRect Dialog { get; }

    public TouchRect DialogTitle { get; }

    public TouchRect DialogClose { get; }

    public TouchRect MoreScroll { get; }

    public TouchRect MoreDatabase { get; }

    public TouchRect MoreHealth { get; }

    public TouchRect MoreStatus { get; }

    public TouchRect AccountTitle { get; }

    public TouchRect AccountToggle { get; }

    public TouchRect AccountStatus { get; }

    public TouchRect AccountPersistence { get; }

    public float AccountFormTop { get; }

    public TouchRect AccountWhy { get; }

    public TouchRect AccountInput { get; }

    public TouchRect AccountSubmit { get; }

    public TouchRect AccountBanner { get; }

    public TouchRect AccountLinked { get; }

    public float MoreContentHeight { get; }

    public TouchRect NoteInput { get; }

    public TouchRect NoteCount { get; }

    public TouchRect NoteCancel { get; }

    public TouchRect NoteSave { get; }

    public TouchRect PagerButton(int index) =>
        _pagerButtons[Math.Clamp(index, 0, PagerButtonCount - 1)];

    public TouchRect ArchiveRow(int index, bool runs)
    {
        var height = runs ? RunRowHeight : BattleRowHeight;
        return new TouchRect(0f, Math.Max(0, index) * height, ArchiveList.Width, height - Gap / 2f);
    }

    public TouchRect RailChip(int index) =>
        Compact
            ? new(Math.Max(0, index) * ChipWidth, 0f, ChipWidth - Gap / 2f, _target)
            : new(0f, Math.Max(0, index) * ChipHeight, RailList.Width, ChipHeight - Gap / 2f);

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

    public HistorySkillsSheetLayout SkillsSheet(IReadOnlyList<int> counts)
    {
        var rows = Math.Clamp(counts.Count, 1, 2);
        var shown = Enumerable
            .Range(0, rows)
            .Select(row => row < counts.Count ? Math.Clamp(counts[row], 0, SkillsPerRow) : 0)
            .ToArray();
        var columns = Math.Max(1, shown.Max());
        var inset = SkillRowInset(Metrics);
        float RowHeight(int row) =>
            2f * inset + BoardTitleHeight + (shown[row] > 0 ? SkillSide : 0f);
        var hint = SkillsHintHeight(Metrics);
        var width = Math.Min(
            Sheet.Width,
            Math.Max(
                Metrics.Dp(360f),
                2f * _padding + 2f * inset + columns * SkillSide + (columns - 1) * Gap
            )
        );
        var height = Math.Min(
            Sheet.Height,
            2f * _padding
                + _target
                + Gap
                + shown.Select((_, row) => RowHeight(row) + Gap).Sum()
                + hint
        );
        var sheet = new TouchRect(
            Sheet.CenterX - width / 2f,
            Sheet.CenterY - height / 2f,
            width,
            height
        );
        var body = sheet.Inset(_padding);
        var close = _chrome.CloseAt(body);
        var rowTop = close.Bottom + Gap;
        var sides = new TouchRect[rows];
        var rowTitles = new TouchRect[rows];
        var cells = new TouchRect[rows, columns];
        for (var row = 0; row < rows; row++)
        {
            sides[row] = new(body.Left, rowTop, body.Width, RowHeight(row));
            rowTitles[row] = new(
                body.Left + inset,
                rowTop + inset,
                body.Width - 2f * inset,
                BoardTitleHeight
            );
            for (var index = 0; index < shown[row]; index++)
                cells[row, index] = new(
                    body.Left + inset + index * (SkillSide + Gap),
                    rowTop + inset + BoardTitleHeight,
                    SkillSide,
                    SkillSide
                );
            rowTop += RowHeight(row) + Gap;
        }
        var hintTop = rowTop;
        return new HistorySkillsSheetLayout(
            sheet,
            _chrome.TitleBeside(body, close),
            close,
            new(body.Left, hintTop, body.Width, Math.Max(0f, body.Bottom - hintTop)),
            sides,
            rowTitles,
            cells
        );
    }

    private static float SkillsHintHeight(TouchMetrics metrics) => metrics.Dp(22f);

    private static float SkillRowInset(TouchMetrics metrics) => metrics.Dp(8f);

    public float SkillScale => SkillSide / NativeCardPrefab.Height;

    public float InspectScale(int span) =>
        _inspect.Scale(
            NativeCardPrefab.SocketWidth * Math.Clamp(span, 1, 3),
            NativeCardPrefab.Height * NativeCardPrefab.FrameHeightOverSocket
        );
}

public sealed record HistorySkillsSheetLayout(
    TouchRect Sheet,
    TouchRect Title,
    TouchRect Close,
    TouchRect Hint,
    TouchRect[] Sides,
    TouchRect[] RowTitles,
    TouchRect[,] Cells
);
