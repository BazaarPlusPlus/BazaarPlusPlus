#nullable enable
using BazaarPlusPlus.Core.Cards;
using BazaarPlusPlus.Core.Ui;

namespace BazaarPlusPlus.Game.HistoryPanel.Ui;

/// <summary>
/// The History panel's geometry on a landscape touch screen, in canvas units, top-down from the
/// canvas's top-left corner: a full sheet inside the safe area. The header carries the section
/// tabs, the drag-scrolled filter chips, More, and Close. Under it stand three columns: the
/// archive (its list, page label, and pager), the run's battle rail with its two pagers, and the
/// detail column with the native boards over the action row (delete, favorite, skills, replay).
/// The status line runs along the bottom of the sheet. Every target is at least
/// <see cref="TouchMetrics.MinTargetDp"/> and every text at least
/// <see cref="TouchMetrics.MinBodySp"/>. The boards keep the game's card prefab proportions: ten
/// sockets span the detail column, which takes <see cref="DetailSocketDp"/> per socket before the
/// archive takes the rest, so a card's footprint stays a full touch target on a 20:9 phone. The
/// skills sheet, the card inspection, the More dialog, and the favorite note dialog sit over the
/// sheet. List rows, rail chips, filter chips, and dialog rows are given from their content's
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

        TitleFont = chrome.TitleFont;
        ButtonFont = chrome.ButtonFont;
        RowTitleFont = chrome.ButtonFont;
        BodyFont = chrome.BodyFont;
        NameFont = chrome.NameFont;

        Sheet = chrome.Sheet;
        var body = chrome.Body;

        // Header: tabs from the left, Close and More from the right, the filters between them.
        Close = chrome.Close;
        var moreWidth = metrics.Target(80f);
        More = new TouchRect(Close.Left - Gap - moreWidth, body.Top, moreWidth, target);
        var tabWidth = metrics.Target(104f);
        RunsTab = new TouchRect(body.Left, body.Top, tabWidth, target);
        GhostTab = new TouchRect(RunsTab.Right + Gap, body.Top, tabWidth, target);
        FavoritesTab = new TouchRect(GhostTab.Right + Gap, body.Top, tabWidth, target);
        FilterStrip = new TouchRect(
            FavoritesTab.Right + Gap,
            body.Top,
            Math.Max(0f, More.Left - Gap - FavoritesTab.Right - Gap),
            target
        );
        HeroChipWidth = metrics.Target(96f);
        OutcomeChipWidth = metrics.Target(80f);
        DayChipWidth = metrics.Target(72f);

        StatusHeight = metrics.Dp(18f);
        Status = new TouchRect(body.Left, body.Bottom - StatusHeight, body.Width, StatusHeight);

        var top = Close.Bottom + Gap;
        var bottom = Status.Top - small;
        var columnHeight = Math.Max(0f, bottom - top);
        var railWidth = metrics.Target(76f);
        var archiveWidth = Math.Clamp(
            body.Width - railWidth - 2f * Gap - SocketCount * metrics.Dp(DetailSocketDp),
            metrics.Dp(200f),
            metrics.Dp(320f)
        );
        Archive = new TouchRect(body.Left, top, archiveWidth, columnHeight);
        Rail = new TouchRect(Archive.Right + Gap, top, railWidth, columnHeight);
        Detail = new TouchRect(
            Rail.Right + Gap,
            top,
            Math.Max(0f, body.Right - Rail.Right - Gap),
            columnHeight
        );

        // Archive: the list over the page label (two lines) over the pager.
        ArchivePager = new TouchRect(Archive.Left, Archive.Bottom - target, archiveWidth, target);
        PagerButtonWidth = (archiveWidth - Gap * (PagerButtonCount - 1)) / PagerButtonCount;
        // Two lines of body text with TMP's line spacing; 32dp clipped the second into an ellipsis.
        var pageLabelHeight = metrics.Dp(40f);
        PageLabel = new TouchRect(
            Archive.Left,
            ArchivePager.Top - small - pageLabelHeight,
            archiveWidth,
            pageLabelHeight
        );
        ArchiveList = new TouchRect(
            Archive.Left,
            top,
            archiveWidth,
            Math.Max(0f, PageLabel.Top - small - top)
        );
        RunRowHeight = metrics.Dp(72f);
        BattleRowHeight = metrics.Dp(64f);

        // Rail: newer on top, older at the bottom, the battles between.
        RailNewer = new TouchRect(Rail.Left, Rail.Top, railWidth, target);
        RailOlder = new TouchRect(Rail.Left, Rail.Bottom - target, railWidth, target);
        RailList = new TouchRect(
            Rail.Left,
            RailNewer.Bottom + small,
            railWidth,
            Math.Max(0f, RailOlder.Top - small - (RailNewer.Bottom + small))
        );
        ChipHeight = metrics.Dp(56f);
        // Ghost has no rail: its cloud note wraps in the rail's column instead of the filter strip,
        // where it ran past the screen's edge.
        CloudNote = new TouchRect(Rail.Left, Rail.Top, railWidth, columnHeight);

        // Detail: the action row at the bottom, the boards above it.
        Footer = new TouchRect(Detail.Left, Detail.Bottom - target, Detail.Width, target);
        Delete = new TouchRect(Detail.Left, Footer.Top, metrics.Target(104f), target);
        Favorite = new TouchRect(Delete.Right + Gap, Footer.Top, metrics.Target(120f), target);
        Skills = new TouchRect(Favorite.Right + Gap, Footer.Top, metrics.Target(96f), target);
        Replay = new TouchRect(
            Skills.Right + Gap,
            Footer.Top,
            Math.Max(0f, Detail.Right - Skills.Right - Gap),
            target
        );

        BoardTitleHeight = metrics.Dp(18f);
        var boardGap = metrics.Dp(2f);
        var boardsHeight = Math.Max(0f, Footer.Top - Gap - Detail.Top);
        var heightScale =
            (boardsHeight - 2f * BoardTitleHeight - boardGap)
            / 2f
            / (NativeCardPrefab.Height * NativeCardPrefab.FrameHeightOverSocket);
        var widthScale = Detail.Width / (NativeCardPrefab.SocketWidth * SocketCount);
        CardScale = Math.Max(0.01f, Math.Min(widthScale, heightScale));
        SocketWidth = NativeCardPrefab.SocketWidth * CardScale;
        CardHeight = NativeCardPrefab.Height * NativeCardPrefab.FrameHeightOverSocket * CardScale;
        CarpetWidth = SocketWidth * SocketCount;
        UpperBoardTitle = new TouchRect(Detail.Left, Detail.Top, Detail.Width, BoardTitleHeight);
        UpperBoard = new TouchRect(Detail.Left, UpperBoardTitle.Bottom, CarpetWidth, CardHeight);
        LowerBoardTitle = new TouchRect(
            Detail.Left,
            UpperBoard.Bottom + boardGap,
            Detail.Width,
            BoardTitleHeight
        );
        LowerBoard = new TouchRect(Detail.Left, LowerBoardTitle.Bottom, CarpetWidth, CardHeight);

        // The card inspection: the shared tap-to-inspect sheet.
        _inspect = chrome.Inspect();
        InspectCardArea = _inspect.CardArea;
        InspectInfo = _inspect.Info;
        InspectClose = _inspect.Close;
        InspectName = _inspect.Name;
        InspectDetail = _inspect.Detail;

        // The skills sheet: both players' skills, one row each, under its title row.
        var skills = body;
        SkillsClose = chrome.CloseAt(skills);
        SkillsTitle = chrome.TitleBeside(skills, SkillsClose);
        SkillSide = Math.Max(
            1f,
            Math.Min(
                metrics.Dp(96f),
                Math.Min(
                    (skills.Width - Gap * (SkillsPerRow - 1)) / SkillsPerRow,
                    (skills.Bottom - SkillsClose.Bottom - Gap - 2f * (BoardTitleHeight + Gap)) / 2f
                )
            )
        );
        _skillsLeft = skills.Left;
        _skillsTop = SkillsClose.Bottom + Gap;
        SkillsHint = new TouchRect(
            skills.Left,
            SkillRowTop(2),
            skills.Width,
            Math.Max(0f, skills.Bottom - SkillRowTop(2))
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
        var healthWidth = Math.Min(metrics.Target(200f), w * 0.45f);
        MoreHealth = new TouchRect(w - healthWidth, 0f, healthWidth, target);
        MoreDatabase = new TouchRect(0f, 0f, Math.Max(0f, MoreHealth.Left - Gap), target);
        MoreStatus = new TouchRect(0f, MoreHealth.Bottom + small, w, line2);
        var toggleWidth = Math.Min(metrics.Target(128f), w * 0.4f);
        // The History panel's own off switch: the launcher hides while History is open.
        MorePanelSwitch = new TouchRect(
            w - toggleWidth,
            MoreStatus.Bottom + Gap,
            toggleWidth,
            target
        );
        MorePanelTitle = new TouchRect(
            0f,
            MorePanelSwitch.Top,
            Math.Max(0f, MorePanelSwitch.Left - Gap),
            target
        );
        AccountToggle = new TouchRect(
            w - toggleWidth,
            MorePanelSwitch.Bottom + Gap,
            toggleWidth,
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
        var submitWidth = Math.Min(metrics.Target(140f), w * 0.4f);
        AccountSubmit = new TouchRect(
            w - submitWidth,
            AccountWhy.Bottom + small,
            submitWidth,
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
    private readonly float _skillsLeft;
    private readonly float _skillsTop;

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

    public TouchRect RunsTab { get; }

    public TouchRect GhostTab { get; }

    public TouchRect FavoritesTab { get; }

    /// <summary>The filter chips' viewport; the chips scroll sideways inside it.</summary>
    public TouchRect FilterStrip { get; }

    public float HeroChipWidth { get; }

    public float OutcomeChipWidth { get; }

    public float DayChipWidth { get; }

    /// <summary>Ghost's cloud note, wrapped in the rail's column, which Ghost leaves empty.</summary>
    public TouchRect CloudNote { get; }

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

    public TouchRect SkillsTitle { get; }

    public TouchRect SkillsClose { get; }

    /// <summary>A skill square's side on the skills sheet.</summary>
    public float SkillSide { get; }

    public TouchRect SkillsHint { get; }

    /// <summary>The More dialog and the note dialog.</summary>
    public TouchRect Dialog { get; }

    public TouchRect DialogTitle { get; }

    public TouchRect DialogClose { get; }

    /// <summary>The More dialog's drag-scrolled viewport; its rows are from its content's top-left.</summary>
    public TouchRect MoreScroll { get; }

    public TouchRect MoreDatabase { get; }

    public TouchRect MoreHealth { get; }

    public TouchRect MoreStatus { get; }

    public TouchRect MorePanelTitle { get; }

    public TouchRect MorePanelSwitch { get; }

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
        new(0f, Math.Max(0, index) * ChipHeight, RailList.Width, ChipHeight - Gap / 2f);

    /// <summary>
    /// Filter chips of the given widths laid side by side, from the strip content's left edge.
    /// </summary>
    public IReadOnlyList<TouchRect> FilterChips(IReadOnlyList<float> widths)
    {
        var chips = new TouchRect[widths.Count];
        var left = 0f;
        for (var index = 0; index < widths.Count; index++)
        {
            chips[index] = new TouchRect(left, 0f, widths[index], FilterStrip.Height);
            left += widths[index] + Gap;
        }
        return chips;
    }

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

    /// <summary>A skills sheet row's title band, in canvas units; row 0 is the opponent's.</summary>
    public TouchRect SkillRowTitle(int row) =>
        new(_skillsLeft, SkillRowTop(row), SkillsHint.Width, BoardTitleHeight);

    /// <summary>One skill square on the skills sheet, in canvas units.</summary>
    public TouchRect SkillCell(int row, int index) =>
        new(
            _skillsLeft + Math.Clamp(index, 0, SkillsPerRow - 1) * (SkillSide + Gap),
            SkillRowTop(row) + BoardTitleHeight,
            SkillSide,
            SkillSide
        );

    /// <summary>The prefab scale that fills a skill square.</summary>
    public float SkillScale => SkillSide / NativeCardPrefab.Height;

    /// <summary>The enlarged card's scale on the inspection for a card <paramref name="span"/> sockets wide.</summary>
    public float InspectScale(int span, bool skill)
    {
        var width = skill
            ? NativeCardPrefab.Height
            : NativeCardPrefab.SocketWidth * Math.Clamp(span, 1, 3);
        var height = skill
            ? NativeCardPrefab.Height
            : NativeCardPrefab.Height * NativeCardPrefab.FrameHeightOverSocket;
        return _inspect.Scale(width, height);
    }

    private float SkillRowTop(int row) =>
        _skillsTop + Math.Clamp(row, 0, 2) * (BoardTitleHeight + SkillSide + Gap);
}
