#nullable enable
using BazaarPlusPlus.Core.Cards;
using BazaarPlusPlus.Core.Ui;

namespace BazaarPlusPlus.Game.HistoryPanel.Ui;

/// <summary>
/// The History panel's geometry on a landscape touch screen, in canvas units, top-down from the
/// canvas's top-left corner: a full sheet inside the safe area. The header carries the title,
/// supporter action, More and Close. Under it stand three columns: the
/// archive (tabs, filters, list, page label and pager), the run's battle rail with its two pagers, and the
/// detail column with the native boards over the action row (delete, set apart from favorite,
/// skills and replay). The rail ends above the action row, so its pager never reads as part of
/// it. The status line runs along the bottom of the sheet. Every target is at least
/// <see cref="TouchMetrics.MinTargetDp"/> and every text at least
/// <see cref="TouchMetrics.MinBodySp"/>. The boards keep the game's card prefab proportions: ten
/// sockets span the detail column, which takes <see cref="DetailSocketDp"/> per socket before the
/// archive takes the rest. When two boards would shrink below a touch target, the compact
/// detail step uses a single board beneath Back, battle navigation and side selection. The
/// skills side picker (sized to the skills it shows), the card inspection, the More dialog, and
/// the favorite note dialog sit over the sheet. List rows, rail chips and dialog rows are given from their content's
/// top-left corner. The desktop's <c>HistoryPanelLayout</c> is a separate layout and is not
/// derived from this one.
/// </summary>
public sealed class HistoryTouchLayout
{
    public const int SocketCount = 10;

    /// <summary>The most skills a skills sheet row shows.</summary>
    public const int SkillsPerRow = 10;

    /// <summary>The socket width the detail column is sized for before the archive takes the rest.</summary>
    public const float DetailSocketDp = 52f;

    public const int PagerButtonCount = 3;

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
        var titleWidth = metrics.Dp(144f);
        var supportWidth = Math.Min(
            metrics.Dp(340f),
            More.Left - body.Left - titleWidth - 2f * Gap
        );
        Sponsor = new TouchRect(More.Left - Gap - supportWidth, body.Top, supportWidth, target);
        Title = new TouchRect(body.Left + small, body.Top, titleWidth - small, target);

        StatusHeight = metrics.Dp(18f);
        Status = new TouchRect(body.Left, body.Bottom - StatusHeight, body.Width, StatusHeight);
        var top = Close.Bottom + Gap;
        var bottom = Status.Top - small;
        var columnHeight = Math.Max(0f, bottom - top);
        var railWidth = metrics.Target(64f);
        var archiveWidth = Math.Clamp(
            body.Width - railWidth - 2f * Gap - SocketCount * metrics.Dp(DetailSocketDp),
            metrics.Dp(200f),
            metrics.Dp(320f)
        );
        var detailWidth = body.Width - archiveWidth - railWidth - 2f * Gap;
        BoardTitleHeight = metrics.Dp(18f);
        var boardGap = metrics.Dp(2f);
        var nativeHeight = NativeCardPrefab.Height * NativeCardPrefab.FrameHeightOverSocket;
        var wideScale = Math.Min(
            detailWidth / (NativeCardPrefab.SocketWidth * SocketCount),
            (columnHeight - target - Gap - 2f * BoardTitleHeight - boardGap) / (2f * nativeHeight)
        );
        Compact = wideScale * NativeCardPrefab.SocketWidth < metrics.MinTarget;
        if (Compact)
            archiveWidth = body.Width;
        Archive = new TouchRect(body.Left, top, archiveWidth, columnHeight);
        TabsTray = new TouchRect(Archive.Left, Archive.Top, Archive.Width, target);
        RunsTab = new TouchRect(TabsTray.Left, TabsTray.Top, TabsTray.Width / 3f, target);
        GhostTab = new TouchRect(RunsTab.Right, TabsTray.Top, RunsTab.Width, target);
        FavoritesTab = new TouchRect(GhostTab.Right, TabsTray.Top, RunsTab.Width, target);
        ArchiveFilter = new TouchRect(
            Archive.Left + small,
            TabsTray.Bottom + small,
            Archive.Width - 2f * small,
            target
        );
        ArchivePager = new TouchRect(
            Archive.Left + small,
            Archive.Bottom - target,
            Archive.Width - 2f * small,
            target
        );
        PagerButtonWidth = (ArchivePager.Width - Gap * (PagerButtonCount - 1)) / PagerButtonCount;
        PageLabel = new TouchRect(
            ArchivePager.Left,
            ArchivePager.Top - small - metrics.Dp(22f),
            ArchivePager.Width,
            metrics.Dp(22f)
        );
        ArchiveList = new TouchRect(
            Archive.Left + small,
            ArchiveFilter.Bottom + small,
            Archive.Width - 2f * small,
            Math.Max(0f, PageLabel.Top - 2f * small - ArchiveFilter.Bottom)
        );
        ArchiveListWithoutFilter = new TouchRect(
            ArchiveList.Left,
            TabsTray.Bottom + small,
            ArchiveList.Width,
            PageLabel.Top - 2f * small - TabsTray.Bottom
        );
        RunRowHeight = metrics.Dp(72f);
        BattleRowHeight = metrics.Dp(72f);

        // The compact detail replaces the archive header with Back and battle navigation,
        // retaining Close. This leaves one full-size native board even on short safe areas.
        Detail = Compact
            ? new TouchRect(body.Left, body.Top, body.Width, bottom - body.Top)
            : new TouchRect(Archive.Right + Gap + railWidth + Gap, top, detailWidth, columnHeight);
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
        ChipHeight = metrics.Dp(56f);
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
        var boardTop = Compact ? Back.Bottom + small : Detail.Top;
        var boardsHeight = Math.Max(0f, Footer.Top - Gap - boardTop);
        var heightScale = Compact
            ? (boardsHeight - BoardTitleHeight) / nativeHeight
            : (boardsHeight - 2f * BoardTitleHeight - boardGap) / (2f * nativeHeight);
        var widthScale = Detail.Width / (NativeCardPrefab.SocketWidth * SocketCount);
        CardScale = Math.Max(.01f, Math.Min(widthScale, heightScale));
        SocketWidth = NativeCardPrefab.SocketWidth * CardScale;
        CardHeight = nativeHeight * CardScale;
        CarpetWidth = SocketWidth * SocketCount;
        var carpetLeft = Detail.Left + (Detail.Width - CarpetWidth) / 2f;
        UpperBoardTitle = new TouchRect(carpetLeft, boardTop, CarpetWidth, BoardTitleHeight);
        UpperBoard = new TouchRect(carpetLeft, UpperBoardTitle.Bottom, CarpetWidth, CardHeight);
        LowerBoardTitle = Compact
            ? UpperBoardTitle
            : new TouchRect(
                carpetLeft,
                UpperBoard.Bottom + boardGap,
                CarpetWidth,
                BoardTitleHeight
            );
        LowerBoard = Compact
            ? UpperBoard
            : new TouchRect(carpetLeft, LowerBoardTitle.Bottom, CarpetWidth, CardHeight);

        // The card inspection: the shared tap-to-inspect sheet.
        _inspect = chrome.Inspect();
        InspectCardArea = _inspect.CardArea;
        InspectInfo = _inspect.Info;
        InspectClose = _inspect.Close;
        InspectName = _inspect.Name;
        InspectDetail = _inspect.Detail;

        // The skills picker: a skill square fits ten to a side's row, two rows under the title.
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

        // The More dialog and the note dialog: centred, the sheet's height, at most 600dp wide.
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
        // Every row's action shares one width, so the buttons line up down the dialog.
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

    public static HistoryTouchLayout For(TouchMetrics metrics) => new(metrics);

    public TouchMetrics Metrics { get; }

    public float Gap { get; }

    public float TitleFont { get; }

    public float ButtonFont { get; }

    public float RowTitleFont { get; }

    public float BodyFont { get; }

    /// <summary>An inspected card's name.</summary>
    public float NameFont { get; }

    /// <summary>The whole panel: the safe area less a margin.</summary>
    public TouchRect Sheet { get; }

    public TouchRect Title { get; }

    public TouchRect TabsTray { get; }

    /// <summary>Record selection opens a separate detail page when two readable boards cannot fit.</summary>
    public bool Compact { get; }
    public TouchRect ArchiveFilter { get; }
    public TouchRect ArchiveListWithoutFilter { get; }
    public TouchRect Back { get; }
    public TouchRect OpponentToggle { get; }
    public TouchRect PlayerToggle { get; }
    public float ChipWidth { get; }

    public TouchRect RunsTab { get; }

    public TouchRect GhostTab { get; }

    public TouchRect FavoritesTab { get; }

    /// <summary>The single supporter-credit button opening the sponsor link directly.</summary>
    public TouchRect Sponsor { get; }

    public TouchRect More { get; }

    public TouchRect Close { get; }

    public float StatusHeight { get; }

    public TouchRect Status { get; }

    public TouchRect Archive { get; }

    public TouchRect ArchiveList { get; }

    public TouchRect PageLabel { get; }

    public TouchRect ArchivePager { get; }

    public float PagerButtonWidth { get; }

    public float RunRowHeight { get; }

    /// <summary>A Ghost or Favorites row.</summary>
    public float BattleRowHeight { get; }

    public TouchRect Rail { get; }

    public TouchRect RailNewer { get; }

    public TouchRect RailList { get; }

    public TouchRect RailOlder { get; }

    /// <summary>The rail list when the run has no other battle page: the whole rail.</summary>
    public TouchRect RailListFull { get; }

    public float ChipHeight { get; }

    public TouchRect Detail { get; }

    public TouchRect Footer { get; }

    public TouchRect Delete { get; }

    public TouchRect Favorite { get; }

    public TouchRect Skills { get; }

    public TouchRect Replay { get; }

    public float BoardTitleHeight { get; }

    public float CardScale { get; }

    public float SocketWidth { get; }

    /// <summary>A rented card's framed height on the boards.</summary>
    public float CardHeight { get; }

    /// <summary>Ten sockets' width: no card reaches past it.</summary>
    public float CarpetWidth { get; }

    /// <summary>The opponent's board with two boards, the challenger's with one (Ghost).</summary>
    public TouchRect UpperBoardTitle { get; }

    public TouchRect UpperBoard { get; }

    /// <summary>Your board, when the section shows two.</summary>
    public TouchRect LowerBoardTitle { get; }

    public TouchRect LowerBoard { get; }

    public TouchRect InspectCardArea { get; }

    public TouchRect InspectInfo { get; }

    public TouchRect InspectName { get; }

    public TouchRect InspectClose { get; }

    public TouchRect InspectDetail { get; }

    /// <summary>A skill square's side on the skills sheet.</summary>
    public float SkillSide { get; }

    /// <summary>The More dialog and the note dialog.</summary>
    public TouchRect Dialog { get; }

    public TouchRect DialogTitle { get; }

    public TouchRect DialogClose { get; }

    /// <summary>The More dialog's drag-scrolled viewport; its rows are from its content's top-left.</summary>
    public TouchRect MoreScroll { get; }

    public TouchRect MoreDatabase { get; }

    public TouchRect MoreHealth { get; }

    /// <summary>The More dialog's status line; the rows below rise into it while it is empty.</summary>
    public TouchRect MoreStatus { get; }

    public TouchRect AccountTitle { get; }

    public TouchRect AccountToggle { get; }

    public TouchRect AccountStatus { get; }

    public TouchRect AccountPersistence { get; }

    /// <summary>Where the account link form starts; the content ends here while it is hidden.</summary>
    public float AccountFormTop { get; }

    public TouchRect AccountWhy { get; }

    public TouchRect AccountInput { get; }

    public TouchRect AccountSubmit { get; }

    public TouchRect AccountBanner { get; }

    public TouchRect AccountLinked { get; }

    /// <summary>The More dialog's content height with the account link form shown.</summary>
    public float MoreContentHeight { get; }

    public TouchRect NoteInput { get; }

    public TouchRect NoteCount { get; }

    public TouchRect NoteCancel { get; }

    public TouchRect NoteSave { get; }

    /// <summary>One archive pager button (newer, older, latest).</summary>
    public TouchRect PagerButton(int index) =>
        new(
            ArchivePager.Left
                + Math.Clamp(index, 0, PagerButtonCount - 1) * (PagerButtonWidth + Gap),
            ArchivePager.Top,
            PagerButtonWidth,
            ArchivePager.Height
        );

    /// <summary>An archive row, from the list content's top-left corner.</summary>
    public TouchRect ArchiveRow(int index, bool runs)
    {
        var height = runs ? RunRowHeight : BattleRowHeight;
        return new TouchRect(0f, Math.Max(0, index) * height, ArchiveList.Width, height - Gap / 2f);
    }

    /// <summary>A battle chip on the rail, from the rail content's top-left corner.</summary>
    public TouchRect RailChip(int index) =>
        Compact
            ? new(Math.Max(0, index) * ChipWidth, 0f, ChipWidth - Gap / 2f, _target)
            : new(0f, Math.Max(0, index) * ChipHeight, RailList.Width, ChipHeight - Gap / 2f);

    /// <summary>
    /// A card's footprint on a board, from the board's top-left corner: <paramref name="span"/>
    /// sockets from <paramref name="socket"/>.
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

    /// <summary>
    /// The skills side picker for one or two sides holding <paramref name="counts"/> skills each,
    /// centred on the sheet and no larger than they need. Each side is one button over its title
    /// and its skills; a side without skills keeps only its title.
    /// </summary>
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

    /// <summary>The prefab scale that fills a skill square.</summary>
    public float SkillScale => SkillSide / NativeCardPrefab.Height;

    /// <summary>The enlarged card's scale on the inspection for a card <paramref name="span"/> sockets wide.</summary>
    public float InspectScale(int span) =>
        _inspect.Scale(
            NativeCardPrefab.SocketWidth * Math.Clamp(span, 1, 3),
            NativeCardPrefab.Height * NativeCardPrefab.FrameHeightOverSocket
        );
}

/// <summary>
/// One opening of the skills side picker, in canvas units: <see cref="Sides"/> is each side's
/// button, holding its <see cref="RowTitles"/> entry and its <see cref="Cells"/>; a cell past a
/// side's count is empty.
/// </summary>
public sealed record HistorySkillsSheetLayout(
    TouchRect Sheet,
    TouchRect Title,
    TouchRect Close,
    TouchRect Hint,
    TouchRect[] Sides,
    TouchRect[] RowTitles,
    TouchRect[,] Cells
);
