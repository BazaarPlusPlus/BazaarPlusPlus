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
    public const float ArchiveLeft = .035f;
    public const float ArchiveWidth = .255f;
    public const float TimelineLeft = .30f;
    public const float TimelineWidth = .09f;
    public const float TimelineTop = .17f;
    public const float TimelineHeight = .65f;
    public const float DetailLeft = .40f;
    public const float DetailWidth = .565f;

    public const float RunRowHeight = 92;
    public const float GhostRowHeight = 116;

    // The chip has a portrait, day and rank across the top, then a name and outcome bar.
    public const float DayChipHeight = 82;
    public const float DayChipOutcomeBar = 3;

    // Both lists end at .82 and share one pager baseline and height.
    public const float PagerTop = .835f;
    public const float PagerHeight = .033f;
    public const float TimelinePagerWidth = (TimelineWidth - .004f) / 2;
    public const float TimelinePagerRightLeft = TimelineLeft + TimelinePagerWidth + .004f;

    public float TimelineRailWidth => Width * TimelineWidth;

    // The detail rail's top-down span: from the first row under the filter strip down to
    // the bottom of the lower board. Everything between is budgeted in canvas units.
    public const float DetailTop = .17f;
    public const float DetailBottom = .849f;

    // Board rects, top-down. Runs shows both; Ghost drops the opponent rect and lets the
    // remaining board take the rail. That remaining board is the CHALLENGER's, not the
    // local player's — ghost payloads stay in recorder perspective (ADR-0002), and
    // HistoryPanel.NativeBoards renders BuildPlayer, which reads Snapshots.PlayerHand.
    // The two boards are peers, so they are the SAME height — an unequal pair reads as a
    // hierarchy that does not exist. Both titles sit tight against their board, and the
    // slack that used to idle between the day strip and the upper title goes to the cards.
    public const float BoardTitleHeight = .042f;
    public const float BoardHeight = .29f;
    public const float OpponentTitleTop = .17f;
    public const float OpponentBoardTop = .217f;
    public const float OpponentBoardHeight = BoardHeight;
    public const float PlayerTitleTop = .512f;
    public const float PlayerBoardTop = .559f;
    public const float PlayerBoardHeight = BoardHeight;
    public const float GhostTitleTop = .17f;
    public const float GhostBoardTop = .217f;
    public const float GhostBoardHeight = .632f;

    // The panel chrome both hosts lay out, top-down. Header: title, the More and Close
    // actions, the section tabs and the filter strip beside them.
    public static readonly HistoryRect Title = new(.04f, .03f, .37f, .06f);
    public static readonly HistoryRect MoreButton = new(.76f, .04f, .09f, .045f);
    public static readonly HistoryRect CloseButton = new(.87f, .04f, .09f, .045f);
    public static readonly HistoryRect RunsTab = new(.04f, .105f, .068f, .038f);
    public static readonly HistoryRect GhostTab = new(.112f, .105f, .068f, .038f);
    public static readonly HistoryRect FavoritesTab = new(.184f, .105f, .068f, .038f);
    public static readonly HistoryRect FilterStrip = new(.255f, .105f, .695f, .038f);

    // The archive column: its page label, list and pager; the timeline's two pagers.
    public static readonly HistoryRect PageLabel = new(ArchiveLeft, .17f, ArchiveWidth, .043f);
    public static readonly HistoryRect ArchiveList = new(ArchiveLeft, .225f, ArchiveWidth, .595f);
    public static readonly HistoryRect ArchivePager = new(
        ArchiveLeft,
        PagerTop,
        ArchiveWidth,
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
    public static readonly HistoryRect DeleteButton = new(.04f, .90f, .16f, .044f);
    public static readonly HistoryRect RecordButton = new(.58f, .90f, .13f, .055f);
    public static readonly HistoryRect ReplayButton = new(.72f, .90f, .23f, .055f);
    public static readonly HistoryRect StatusLabel = new(.04f, .953f, .91f, .03f);

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
