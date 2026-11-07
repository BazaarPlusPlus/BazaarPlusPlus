using BazaarPlusPlus.Game.HistoryPanel.Ui;
using Xunit;

public sealed class HistoryPanelLayoutTests
{
    [Fact]
    public void Header_groups_do_not_overlap_and_utility_actions_share_one_size()
    {
        var groups = new[]
        {
            HistoryPanelLayout.Title,
            HistoryPanelLayout.TabsTray,
            HistoryPanelLayout.SupportButton,
            HistoryPanelLayout.MoreButton,
            HistoryPanelLayout.CloseButton,
            HistoryPanelLayout.FilterStrip,
        };
        for (var i = 0; i < groups.Length; i++)
        for (var j = i + 1; j < groups.Length; j++)
            Assert.False(groups[i].Anchors.Overlaps(groups[j].Anchors));
        Assert.True(HistoryPanelLayout.SupportButton.Width > HistoryPanelLayout.MoreButton.Width);
        Assert.True(HistoryPanelLayout.Title.Bottom < HistoryPanelLayout.TabsTray.Y);
        Assert.Equal(HistoryPanelLayout.ArchiveList.X, HistoryPanelLayout.TabsTray.X);
        Assert.Equal(HistoryPanelLayout.ArchiveList.Width, HistoryPanelLayout.TabsTray.Width);
        Assert.Equal(HistoryPanelLayout.MoreButton.Width, HistoryPanelLayout.CloseButton.Width);
        Assert.Equal(
            HistoryPanelLayout.SupportButton.Height,
            HistoryPanelLayout.CloseButton.Height
        );
    }

    // Aspect ratios, not resolutions: the panel's geometry is normalized, so it is identical
    // at every resolution of a given ratio. What changes is how many canvas units a
    // normalized value is worth, because matchWidthOrHeight = .5 makes the canvas the
    // geometric mean of both axes.
    public static TheoryData<float, float> AspectRatios =>
        new()
        {
            { 1600, 1000 },
            { 1920, 1080 },
            { 1600, 1200 },
            { 3440, 1440 },
            { 5120, 1440 },
        };

    private static HistoryAnchors DetailRect(float top, float height) =>
        HistoryPanelLayout.Anchors(
            HistoryPanelLayout.DetailLeft,
            top,
            HistoryPanelLayout.DetailWidth,
            height
        );

    [Theory]
    [MemberData(nameof(AspectRatios))]
    public void Canvas_units_follow_the_geometric_mean_of_both_axes(float width, float height)
    {
        var panel = new HistoryPanelLayout(width, height);
        var expected = (float)
            System.Math.Sqrt(
                width
                    / HistoryPanelLayout.ReferenceWidth
                    * (height / HistoryPanelLayout.ReferenceHeight)
            );

        Assert.Equal(expected, panel.CanvasScale, precision: 4);
        Assert.Equal(width / expected, panel.Width, precision: 2);
        Assert.Equal(height / expected, panel.Height, precision: 2);
    }

    [Fact]
    public void Reference_resolution_maps_one_to_one()
    {
        var panel = new HistoryPanelLayout(
            HistoryPanelLayout.ReferenceWidth,
            HistoryPanelLayout.ReferenceHeight
        );

        Assert.Equal(1, panel.CanvasScale, precision: 4);
        Assert.Equal(HistoryPanelLayout.ReferenceWidth, panel.Width, precision: 2);
        Assert.Equal(HistoryPanelLayout.ReferenceHeight, panel.Height, precision: 2);
    }

    [Fact]
    public void Columns_stay_ordered_and_inside_the_panel()
    {
        Assert.True(
            HistoryPanelLayout.ArchiveLeft + HistoryPanelLayout.ArchiveWidth
                <= HistoryPanelLayout.TimelineLeft
        );
        Assert.True(
            HistoryPanelLayout.TimelineLeft + HistoryPanelLayout.TimelineWidth
                <= HistoryPanelLayout.DetailLeft
        );
        Assert.True(HistoryPanelLayout.DetailLeft + HistoryPanelLayout.DetailWidth <= 1);
    }

    [Theory]
    [MemberData(nameof(AspectRatios))]
    public void Run_row_and_detail_rail_keep_a_usable_width_at_every_ratio(
        float width,
        float height
    )
    {
        var panel = new HistoryPanelLayout(width, height);

        // 4:3 is the narrowest canvas. The compact archive still reserves room for the
        // portrait, three text lines, a rank with rating and the outcome trophy.
        var runRowWidth = panel.Width * HistoryPanelLayout.ArchiveWidth;
        Assert.True(runRowWidth >= 320, $"run row {runRowWidth}");

        // The boards are width-constrained, so this is the number that sets card size.
        var detailRailWidth = panel.Width * HistoryPanelLayout.DetailWidth;
        Assert.True(detailRailWidth >= 680, $"detail rail {detailRailWidth}");
    }

    [Theory]
    [MemberData(nameof(AspectRatios))]
    public void Timeline_rail_stays_wide_enough_for_a_portrait_beside_a_rank(
        float width,
        float height
    )
    {
        var panel = new HistoryPanelLayout(width, height);

        // The chip is an opponent portrait on the left with the day and rank stacked to its
        // right. Below this the portrait stops being recognizable and the rail is just noise.
        Assert.True(panel.TimelineRailWidth >= 100, $"rail {panel.TimelineRailWidth}");

        // Even the shortest canvas must show enough battles to read as a timeline;
        // the rail is never the scarcer list beside the archive.
        var rows =
            panel.Height * HistoryPanelLayout.TimelineHeight / HistoryPanelLayout.DayChipHeight;
        var archiveRows =
            panel.Height * HistoryPanelLayout.ArchiveList.Height / HistoryPanelLayout.RunRowHeight;
        Assert.True(rows >= 5, $"rows {rows}");
        Assert.True(rows > archiveRows, $"rail {rows} vs archive {archiveRows}");
    }

    [Fact]
    public void Timeline_pagers_sit_under_the_rail_and_share_its_width()
    {
        Assert.Equal(
            HistoryPanelLayout.TimelineTop + HistoryPanelLayout.TimelineHeight,
            HistoryPanelLayout.ArchiveList.Bottom,
            precision: 5
        );
        Assert.InRange(
            HistoryPanelLayout.PageLabel.Y - HistoryPanelLayout.ArchiveList.Bottom,
            0,
            .01f
        );
        Assert.InRange(
            HistoryPanelLayout.ArchivePager.Y - HistoryPanelLayout.PageLabel.Bottom,
            0,
            .01f
        );
        Assert.True(
            HistoryPanelLayout.PagerTop
                >= HistoryPanelLayout.TimelineTop + HistoryPanelLayout.TimelineHeight
        );
        foreach (
            var action in new[]
            {
                HistoryPanelLayout.DeleteButton,
                HistoryPanelLayout.FavoriteButton,
                HistoryPanelLayout.RecordButton,
                HistoryPanelLayout.ReplayButton,
            }
        )
        {
            Assert.Equal(HistoryPanelLayout.PagerTop, action.Y);
            Assert.Equal(HistoryPanelLayout.PagerHeight, action.Height);
        }
        Assert.True(
            HistoryPanelLayout.PagerTop + HistoryPanelLayout.PagerHeight
                <= HistoryPanelLayout.StatusLabel.Y
        );
        Assert.True(
            HistoryPanelLayout.TimelinePagerRightLeft + HistoryPanelLayout.TimelinePagerWidth
                <= HistoryPanelLayout.TimelineLeft + HistoryPanelLayout.TimelineWidth + .001f
        );
    }

    [Fact]
    public void The_two_boards_are_peers_and_therefore_the_same_height()
    {
        // An unequal pair reads as a hierarchy between the players that does not exist.
        Assert.Equal(
            HistoryPanelLayout.OpponentBoardHeight,
            HistoryPanelLayout.PlayerBoardHeight,
            precision: 5
        );
        // And neither may end up smaller than the single board Ghost shows.
        Assert.True(HistoryPanelLayout.GhostBoardHeight > HistoryPanelLayout.BoardHeight);
    }

    [Fact]
    public void Ownership_titles_frame_the_pair_and_stay_close_to_their_boards()
    {
        Assert.True(
            HistoryPanelLayout.OpponentTitleTop + HistoryPanelLayout.BoardTitleHeight
                <= HistoryPanelLayout.OpponentBoardTop
        );
        Assert.True(
            HistoryPanelLayout.PlayerBoardTop + HistoryPanelLayout.PlayerBoardHeight
                <= HistoryPanelLayout.PlayerTitleTop
        );
        Assert.True(
            HistoryPanelLayout.GhostTitleTop + HistoryPanelLayout.BoardTitleHeight
                <= HistoryPanelLayout.GhostBoardTop
        );
        // A title that drifts far from its board stops reading as that board's label.
        Assert.True(
            HistoryPanelLayout.PlayerTitleTop
                - (HistoryPanelLayout.PlayerBoardTop + HistoryPanelLayout.PlayerBoardHeight)
                < .02f
        );
        Assert.InRange(
            HistoryPanelLayout.PlayerBoardTop
                - (HistoryPanelLayout.OpponentBoardTop + HistoryPanelLayout.OpponentBoardHeight),
            .03f,
            .06f
        );
    }

    [Fact]
    public void Every_detail_rail_element_is_disjoint_in_both_sections()
    {
        var runs = new[]
        {
            DetailRect(HistoryPanelLayout.OpponentTitleTop, HistoryPanelLayout.BoardTitleHeight),
            DetailRect(HistoryPanelLayout.OpponentBoardTop, HistoryPanelLayout.OpponentBoardHeight),
            DetailRect(HistoryPanelLayout.PlayerTitleTop, HistoryPanelLayout.BoardTitleHeight),
            DetailRect(HistoryPanelLayout.PlayerBoardTop, HistoryPanelLayout.PlayerBoardHeight),
        };
        var ghost = new[]
        {
            DetailRect(HistoryPanelLayout.GhostTitleTop, HistoryPanelLayout.BoardTitleHeight),
            DetailRect(HistoryPanelLayout.GhostBoardTop, HistoryPanelLayout.GhostBoardHeight),
        };

        foreach (var section in new[] { runs, ghost })
            for (var i = 0; i < section.Length; i++)
            for (var j = i + 1; j < section.Length; j++)
                Assert.False(section[i].Overlaps(section[j]), $"{i} overlaps {j}");
    }

    [Fact]
    public void Both_board_rects_sit_inside_the_detail_rail_span()
    {
        Assert.True(HistoryPanelLayout.OpponentTitleTop >= HistoryPanelLayout.DetailTop);
        Assert.True(
            HistoryPanelLayout.PlayerTitleTop + HistoryPanelLayout.BoardTitleHeight
                <= HistoryPanelLayout.DetailBottom + .001f
        );
        Assert.True(
            HistoryPanelLayout.GhostBoardTop + HistoryPanelLayout.GhostBoardHeight
                <= HistoryPanelLayout.DetailBottom + .001f
        );
    }

    [Fact]
    public void Boards_do_not_overlap_their_neighbours_in_either_section()
    {
        var opponent = DetailRect(
            HistoryPanelLayout.OpponentBoardTop,
            HistoryPanelLayout.OpponentBoardHeight
        );
        var player = DetailRect(
            HistoryPanelLayout.PlayerBoardTop,
            HistoryPanelLayout.PlayerBoardHeight
        );
        var ghost = DetailRect(
            HistoryPanelLayout.GhostBoardTop,
            HistoryPanelLayout.GhostBoardHeight
        );

        Assert.False(opponent.Overlaps(player));
        // Ghost hides the opponent rect and lets the remaining board take the whole rail.
        Assert.True(ghost.Height > player.Height);
        Assert.True(ghost.Height > opponent.Height);
        Assert.True(ghost.MinY >= 0 && ghost.MaxY <= 1);
    }

    [Fact]
    public void Anchors_flip_top_down_authoring_into_unity_bottom_up_space()
    {
        // A rect authored .59 from the top, .25 tall, is .16 .. .41 in anchor space —
        // the conversion that used to be done by hand at the section-switch call site.
        var anchors = HistoryPanelLayout.Anchors(.39f, .59f, .56f, .25f);

        Assert.Equal(.39f, anchors.MinX, precision: 4);
        Assert.Equal(.95f, anchors.MaxX, precision: 4);
        Assert.Equal(.16f, anchors.MinY, precision: 4);
        Assert.Equal(.41f, anchors.MaxY, precision: 4);
    }

    [Fact]
    public void Overlap_detection_is_exclusive_at_shared_edges()
    {
        var top = HistoryPanelLayout.Anchors(0, 0, 1, .5f);
        var bottom = HistoryPanelLayout.Anchors(0, .5f, 1, .5f);
        var straddling = HistoryPanelLayout.Anchors(0, .4f, 1, .3f);

        Assert.False(top.Overlaps(bottom));
        Assert.True(top.Overlaps(straddling));
        Assert.True(bottom.Overlaps(straddling));
    }
}
