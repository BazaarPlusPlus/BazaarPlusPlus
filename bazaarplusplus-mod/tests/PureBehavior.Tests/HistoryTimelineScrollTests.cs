using BazaarPlusPlus.Game.HistoryPanel.Ui;
using Xunit;

public sealed class HistoryTimelineScrollTests
{
    private const float Viewport = 639;
    private const int Count = 14;

    [Theory]
    [InlineData(3)]
    [InlineData(6)]
    [InlineData(10)]
    public void Clicking_a_visible_row_preserves_the_exact_scroll_offset(int slot)
    {
        var scroll = new HistoryTimelineScroll();
        scroll.Bind("previous", 7, Count, 237.5f, Viewport);

        // Slots 3 and 10 are near opposite edges; 10 is only partly visible.
        Assert.Equal(237.5f, scroll.Bind("clicked", slot, Count, 237.5f, Viewport));
    }

    [Fact]
    public void Clicking_a_row_clipped_at_the_top_does_not_snap_it_into_place()
    {
        var scroll = new HistoryTimelineScroll();

        Assert.Equal(270.25f, scroll.Bind("clicked", 3, Count, 270.25f, Viewport));
    }

    [Fact]
    public void Detail_or_badge_refresh_does_not_undo_manual_scrolling_away_from_selection()
    {
        var scroll = new HistoryTimelineScroll();
        scroll.Bind("selected", 13, Count, 0, Viewport);

        Assert.Equal(15.75f, scroll.Bind("selected", 13, Count, 15.75f, Viewport));
        Assert.Equal(0, scroll.Bind("selected", 13, Count, 0, Viewport));
    }

    [Theory]
    [InlineData(0, 300, 0)]
    [InlineData(2, 300, 164)]
    [InlineData(10, 0, 258)]
    [InlineData(13, 0, 504)]
    public void A_new_selection_outside_the_viewport_moves_only_to_the_nearest_edge(
        int slot,
        float offset,
        float expected
    )
    {
        var scroll = new HistoryTimelineScroll();

        Assert.Equal(expected, scroll.Bind("new-page-selection", slot, Count, offset, Viewport));
    }

    [Fact]
    public void An_empty_loading_page_preserves_offset_and_selection_until_rows_return()
    {
        var scroll = new HistoryTimelineScroll();
        scroll.Bind("selected", 8, Count, 237.5f, Viewport);
        Assert.Equal(0, scroll.Bind(null, -1, 0, 237.5f, Viewport));
        Assert.Equal(0, scroll.Bind(null, -1, 0, 0, Viewport));

        Assert.Equal(237.5f, scroll.Bind("selected", 8, Count, 0, Viewport));
        Assert.Equal(99.25f, scroll.Bind("selected", 8, Count, 99.25f, Viewport));
    }

    [Fact]
    public void A_different_run_after_loading_reveals_its_selected_battle_once()
    {
        var scroll = new HistoryTimelineScroll();
        scroll.Bind("old-run", 4, Count, 150.5f, Viewport);
        scroll.Bind(null, -1, 0, 150.5f, Viewport);

        Assert.Equal(504, scroll.Bind("new-run", 13, Count, 0, Viewport));
        Assert.Equal(250.25f, scroll.Bind("new-run", 13, Count, 250.25f, Viewport));
    }

    [Theory]
    [InlineData(3, 0)]
    [InlineData(10, 181)]
    public void A_shorter_page_clamps_to_the_available_scroll_range(int count, float expected)
    {
        var scroll = new HistoryTimelineScroll();
        scroll.Bind("selected", 8, Count, 400.5f, Viewport);

        Assert.Equal(expected, scroll.Bind("selected", count - 1, count, 400.5f, Viewport));
    }

    [Fact]
    public void Initial_layout_without_viewport_height_does_not_consume_selection_reveal()
    {
        var scroll = new HistoryTimelineScroll();
        Assert.Equal(0, scroll.Bind("selected", 13, Count, 0, 0));

        Assert.Equal(504, scroll.Bind("selected", 13, Count, 0, Viewport));
    }
}
