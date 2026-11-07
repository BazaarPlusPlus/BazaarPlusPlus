#nullable enable
namespace BazaarPlusPlus.Game.HistoryPanel.Ui;

// A rect in Unity anchor space: origin bottom-left, both axes normalized to the parent.
public readonly record struct HistoryAnchors(float MinX, float MinY, float MaxX, float MaxY)
{
    public float Width => MaxX - MinX;
    public float Height => MaxY - MinY;

    public bool Overlaps(HistoryAnchors other) =>
        MinX < other.MaxX && other.MinX < MaxX && MinY < other.MaxY && other.MinY < MaxY;
}

// A rect as the view authors it: top-down, (X, Y) from the panel's top-left, normalized.
public readonly record struct HistoryRect(float X, float Y, float Width, float Height)
{
    public float Bottom => Y + Height;

    public HistoryAnchors Anchors => HistoryPanelLayout.Anchors(X, Y, Width, Height);
}

// The panel's geometry, free of Unity types so the test project can compile it directly.
//
// Two conventions meet here, and mixing them was the panel's long-standing trap: every
// authored value is TOP-DOWN (y grows downward from the panel's top edge, as CreateRect
// takes it), while Unity anchors are BOTTOM-UP. `Anchors` is the only place that flips.
public readonly record struct HistoryPanelLayout(float ScreenWidth, float ScreenHeight)
{
    public const float ReferenceWidth = 1600;
    public const float ReferenceHeight = 1000;

    // HistoryPanelView sets CanvasScaler.matchWidthOrHeight to this. Unlike LiveBuildPanel
    // (which matches height and therefore always sees a 1000-unit-tall canvas), .5 makes the
    // canvas the geometric mean of both axes, so a normalized value is worth a different
    // number of canvas units at every aspect ratio. Nothing here may assume 1600x1000.
    public const float Match = .5f;

    // Columns, normalized on x. The battle timeline is a narrow vertical rail between the
    // archive and the boards — the gap that was otherwise dead space.
    //
    // The boards are WIDTH-constrained: the carpet wants 2600:550, so the detail region
    // scales by width and leaves vertical slack. Every unit this rail takes therefore comes
    // straight out of card size; widening it is not free.
    public const float ArchiveLeft = .025f;
    public const float ArchiveWidth = .22f;
    public const float ArchiveContentLeft = .033f;
    public const float ArchiveContentWidth = .204f;
    public const float TimelineLeft = .255f;
    public const float TimelineWidth = .075f;
    public const float TimelineTop = .115f;
    public const float ListBottom = .86f;
    public const float TimelineHeight = ListBottom - TimelineTop;
    public const float DetailLeft = .345f;
    public const float DetailWidth = .63f;

    public const float RunRowHeight = 92;
    public const float GhostRowHeight = 116;

    // Two lines: result/day, then portrait/name/rank. No independent middle metadata row.
    public const float DayChipHeight = 64;

    // Paging and detail actions share one footer baseline and visible control height.
    public const float PagerTop = .90f;
    public const float PagerHeight = .05f;
    public const float TimelinePagerWidth = (TimelineWidth - .004f) / 2;
    public const float TimelinePagerRightLeft = TimelineLeft + TimelinePagerWidth + .004f;

    public float TimelineRailWidth => Width * TimelineWidth;

    // The detail rail's top-down span: from the first row under the filter strip down to
    // the bottom of the lower board. Everything between is budgeted in canvas units.
    public const float DetailTop = .115f;
    public const float DetailBottom = .849f;

    // Board rects, top-down. Runs shows both; Ghost drops the opponent rect and lets the
    // remaining board take the rail. That remaining board is the CHALLENGER's, not the
    // local player's — ghost payloads stay in recorder perspective (ADR-0002), and
    // HistoryPanel.NativeBoards renders BuildPlayer, which reads Snapshots.PlayerHand.
    // The two boards are peers, so they are the SAME height — an unequal pair reads as a
    // hierarchy that does not exist. Ownership titles frame the pair from outside, leaving
    // only a compact gap between the boards. The lower title follows its skills as well.
    public const float BoardTitleHeight = .042f;
    public const float BoardHeight = .29f;
    public const float OpponentTitleTop = .115f;
    public const float OpponentBoardTop = .162f;
    public const float OpponentBoardHeight = BoardHeight;
    public const float PlayerBoardTop = OpponentBoardTop + BoardHeight + .045f;
    public const float PlayerBoardHeight = BoardHeight;
    public const float PlayerTitleTop = PlayerBoardTop + PlayerBoardHeight + .008f;
    public const float GhostTitleTop = .115f;
    public const float GhostBoardTop = .162f;
    public const float GhostBoardHeight = .687f;

    // The header owns only the title, one sponsor link and the two utility actions.
    // Tabs, filters, records and paging share the archive's visible boundary below it.
    public static readonly HistoryRect Title = new(.033f, .04f, .22f, .045f);
    public static readonly HistoryRect SupportButton = new(.455f, .04f, .36f, .045f);
    public static readonly HistoryRect MoreButton = new(.83f, .04f, .06f, .045f);
    public static readonly HistoryRect CloseButton = new(.905f, .04f, .06f, .045f);
    public static readonly HistoryRect TabsTray = new(
        ArchiveContentLeft,
        .115f,
        ArchiveContentWidth,
        .05f
    );
    public static readonly HistoryRect RunsTab = new(
        ArchiveContentLeft,
        .115f,
        ArchiveContentWidth / 3f,
        .05f
    );
    public static readonly HistoryRect GhostTab = new(
        ArchiveContentLeft + ArchiveContentWidth / 3f,
        .115f,
        ArchiveContentWidth / 3f,
        .05f
    );
    public static readonly HistoryRect FavoritesTab = new(
        ArchiveContentLeft + 2f * ArchiveContentWidth / 3f,
        .115f,
        ArchiveContentWidth / 3f,
        .05f
    );
    public static readonly HistoryRect FilterStrip = new(
        ArchiveContentLeft,
        .175f,
        ArchiveContentWidth,
        .045f
    );

    // The archive column: its page label, list and pager; the timeline's two pagers.
    public static readonly HistoryRect PageLabel = new(
        ArchiveContentLeft,
        .866f,
        ArchiveContentWidth,
        .027f
    );
    public static readonly HistoryRect ArchiveList = new(
        ArchiveContentLeft,
        .23f,
        ArchiveContentWidth,
        ListBottom - .23f
    );
    public static readonly HistoryRect ArchivePager = new(
        ArchiveContentLeft,
        PagerTop,
        ArchiveContentWidth,
        PagerHeight
    );
    public static readonly HistoryRect TimelineNewer = new(
        TimelineLeft,
        PagerTop,
        TimelinePagerWidth,
        PagerHeight
    );
    public static readonly HistoryRect TimelineOlder = new(
        TimelinePagerRightLeft,
        PagerTop,
        TimelinePagerWidth,
        PagerHeight
    );

    // Footer: the delete, record and replay actions, then the status line.
    public static readonly HistoryRect DeleteButton = new(DetailLeft, PagerTop, .09f, PagerHeight);
    public static readonly HistoryRect FavoriteButton = new(.55f, PagerTop, .15f, PagerHeight);
    public static readonly HistoryRect RecordButton = new(.71f, PagerTop, .10f, PagerHeight);
    public static readonly HistoryRect ReplayButton = new(.82f, PagerTop, .155f, PagerHeight);
    public static readonly HistoryRect StatusLabel = new(.033f, .958f, .942f, .025f);

    public float CanvasScale =>
        (float)
            System.Math.Sqrt(
                (double)ScreenWidth / ReferenceWidth * ((double)ScreenHeight / ReferenceHeight)
            );

    public float Width => ScreenWidth / CanvasScale;
    public float Height => ScreenHeight / CanvasScale;

    // Top-down (x, y, width, height) as the view authors it, to Unity's bottom-up anchors.
    public static HistoryAnchors Anchors(float x, float y, float width, float height) =>
        new(x, 1 - y - height, x + width, 1 - y);
}
