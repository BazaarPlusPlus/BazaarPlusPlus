#nullable enable
namespace BazaarPlusPlus.Game.HistoryPanel.Ui;

// A rect in Unity anchor space: origin bottom-left, both axes normalized to the parent.
internal readonly record struct HistoryAnchors(float MinX, float MinY, float MaxX, float MaxY)
{
    internal float Width => MaxX - MinX;
    internal float Height => MaxY - MinY;

    internal bool Overlaps(HistoryAnchors other) =>
        MinX < other.MaxX && other.MinX < MaxX && MinY < other.MaxY && other.MinY < MaxY;
}

// The panel's geometry, free of Unity types so the test project can compile it directly.
//
// Two conventions meet here, and mixing them was the panel's long-standing trap: every
// authored value is TOP-DOWN (y grows downward from the panel's top edge, as CreateRect
// takes it), while Unity anchors are BOTTOM-UP. `Anchors` is the only place that flips.
internal readonly record struct HistoryPanelLayout(float ScreenWidth, float ScreenHeight)
{
    internal const float ReferenceWidth = 1600;
    internal const float ReferenceHeight = 1000;

    // HistoryPanelView sets CanvasScaler.matchWidthOrHeight to this. Unlike LiveBuildPanel
    // (which matches height and therefore always sees a 1000-unit-tall canvas), .5 makes the
    // canvas the geometric mean of both axes, so a normalized value is worth a different
    // number of canvas units at every aspect ratio. Nothing here may assume 1600x1000.
    internal const float Match = .5f;

    // Columns, normalized on x. The battle timeline is a narrow vertical rail between the
    // archive and the boards — the gap that was otherwise dead space.
    //
    // The boards are WIDTH-constrained: the carpet wants 2600:550, so the detail region
    // scales by width and leaves vertical slack. Every unit this rail takes therefore comes
    // straight out of card size; widening it is not free.
    internal const float ArchiveLeft = .035f;
    internal const float ArchiveWidth = .255f;
    internal const float TimelineLeft = .30f;
    internal const float TimelineWidth = .09f;
    internal const float TimelineTop = .17f;
    internal const float TimelineHeight = .65f;
    internal const float DetailLeft = .40f;
    internal const float DetailWidth = .565f;

    internal const float RunRowHeight = 92;
    internal const float GhostRowHeight = 116;

    // The chip has a portrait, day and rank across the top, then a name and outcome bar.
    internal const float DayChipHeight = 82;
    internal const float DayChipOutcomeBar = 3;

    // Both lists end at .82 and share one pager baseline and height.
    internal const float PagerTop = .835f;
    internal const float PagerHeight = .033f;
    internal const float TimelinePagerWidth = (TimelineWidth - .004f) / 2;
    internal const float TimelinePagerRightLeft = TimelineLeft + TimelinePagerWidth + .004f;

    internal float TimelineRailWidth => Width * TimelineWidth;

    // The detail rail's top-down span: from the first row under the filter strip down to
    // the bottom of the lower board. Everything between is budgeted in canvas units.
    internal const float DetailTop = .17f;
    internal const float DetailBottom = .849f;

    // Board rects, top-down. Runs shows both; Ghost drops the opponent rect and lets the
    // remaining board take the rail. That remaining board is the CHALLENGER's, not the
    // local player's — ghost payloads stay in recorder perspective (ADR-0002), and
    // HistoryPanel.NativeBoards renders BuildPlayer, which reads Snapshots.PlayerHand.
    // The two boards are peers, so they are the SAME height — an unequal pair reads as a
    // hierarchy that does not exist. Both titles sit tight against their board, and the
    // slack that used to idle between the day strip and the upper title goes to the cards.
    internal const float BoardTitleHeight = .042f;
    internal const float BoardHeight = .29f;
    internal const float OpponentTitleTop = .17f;
    internal const float OpponentBoardTop = .217f;
    internal const float OpponentBoardHeight = BoardHeight;
    internal const float PlayerTitleTop = .512f;
    internal const float PlayerBoardTop = .559f;
    internal const float PlayerBoardHeight = BoardHeight;
    internal const float GhostTitleTop = .17f;
    internal const float GhostBoardTop = .217f;
    internal const float GhostBoardHeight = .632f;

    internal float CanvasScale =>
        (float)
            System.Math.Sqrt(
                (double)ScreenWidth / ReferenceWidth * ((double)ScreenHeight / ReferenceHeight)
            );

    internal float Width => ScreenWidth / CanvasScale;
    internal float Height => ScreenHeight / CanvasScale;

    // Canvas units for the widths a row's contents must actually fit into.
    internal float RunRowWidth => Width * ArchiveWidth;
    internal float DetailRailWidth => Width * DetailWidth;
    internal float DetailRailHeight => Height * (DetailBottom - DetailTop);

    // Top-down (x, y, width, height) as the view authors it, to Unity's bottom-up anchors.
    internal static HistoryAnchors Anchors(float x, float y, float width, float height) =>
        new(x, 1 - y - height, x + width, 1 - y);
}
