using BazaarPlusPlus.Game.CollectionPanel;
using BazaarPlusPlus.GameInterop.Tooltips;
using Xunit;

namespace PureBehavior.Tests;

public sealed class CollectionInspectionTests
{
    [Theory]
    [InlineData(400f, 600f, 1000f, 1200f, 1f)]
    [InlineData(400f, 2000f, 800f, 1000f, .5f)]
    [InlineData(1000f, 600f, 500f, 1200f, .5f)]
    public void Description_keeps_native_scale_and_shrinks_only_when_needed(
        float width,
        float height,
        float availableWidth,
        float availableHeight,
        float expected
    )
    {
        Assert.Equal(
            expected,
            NativeInspectionSizing.FitScale(width, height, availableWidth, availableHeight)
        );
    }

    [Fact]
    public void Only_the_latest_selection_in_the_current_inspection_can_publish()
    {
        var state = new CollectionInspectionState();
        Assert.False(state.BlocksInput(0));
        var first = state.Open();
        Assert.True(state.CanPublish(first));
        var next = state.Select();
        Assert.False(state.CanPublish(first));
        Assert.True(state.CanPublish(next));
        state.Close(12);
        Assert.False(state.CanPublish(next));
        Assert.True(state.BlocksInput(12));
        Assert.False(state.BlocksInput(13));
        var reopened = state.Open();
        Assert.False(state.CanPublish(next));
        Assert.True(state.CanPublish(reopened));
        Assert.True(state.BlocksInput(13));
    }

    [Theory]
    [InlineData(1920f, 1080f)]
    [InlineData(1280f, 1024f)]
    [InlineData(3440f, 1440f)]
    [InlineData(1080f, 1920f)]
    public void Only_card_moves_right_with_both_contents_inside_the_window(
        float width,
        float height
    )
    {
        var layout = CollectionInspectionLayout.ForContent(width, height, 600f, 900f, 750f, 1400f);
        var detailLeft = layout.DetailCenterX - layout.DetailWidth / 2f;
        Assert.True(layout.CardX >= 0f);
        Assert.True(layout.CardY >= 0f);
        Assert.True(layout.CardX + layout.CardWidth <= width);
        Assert.True(layout.DetailCenterX + layout.DetailWidth / 2f <= width);
        Assert.True(layout.CardY + layout.CardHeight <= height);
        Assert.True(layout.DetailCenterY - layout.DetailHeight / 2f >= 0f);
        Assert.True(layout.DetailCenterY + layout.DetailHeight / 2f <= height);
        Assert.Equal(layout.Gap, detailLeft - layout.CardX - layout.CardWidth, 3);
        var expectedLeft = width - layout.DetailCenterX - layout.DetailWidth / 2f + width * .08f;
        Assert.InRange(Math.Abs(layout.CardX - expectedLeft), 0f, .001f);
    }

    [Fact]
    public void Small_and_large_cards_keep_the_same_gap_with_the_rightward_offset()
    {
        var small = CollectionInspectionLayout.ForContent(2560f, 1600f, 400f, 1000f, 650f, 600f);
        var large = CollectionInspectionLayout.ForContent(2560f, 1600f, 1200f, 1000f, 650f, 600f);
        Assert.Equal(small.Gap, large.Gap);
        Assert.Equal(
            small.Gap,
            small.DetailCenterX - small.DetailWidth / 2f - small.CardX - small.CardWidth,
            3
        );
        Assert.Equal(
            large.Gap,
            large.DetailCenterX - large.DetailWidth / 2f - large.CardX - large.CardWidth,
            3
        );
        Assert.True(small.CardX > large.CardX);
    }

    [Fact]
    public void Enchantment_description_changes_never_move_or_resize_the_inspected_card()
    {
        var anchor = new CollectionInspectionCardAnchor();
        var original = anchor.Resolve(
            2560f,
            1600f,
            CollectionInspectionLayout.ForContent(2560f, 1600f, 400f, 1000f, 650f, 400f)
        );
        foreach (
            var size in new[]
            {
                (Width: 700f, Height: 1800f),
                (Width: 1200f, Height: 2600f),
                (Width: 650f, Height: 400f),
            }
        )
        {
            var candidate = CollectionInspectionLayout.ForContent(
                2560f,
                1600f,
                400f,
                1000f,
                size.Width,
                size.Height
            );
            var actual = anchor.Resolve(2560f, 1600f, candidate);
            Assert.Equal(original, actual);
        }
    }

    [Theory]
    [InlineData(400f, 1000f)]
    [InlineData(450f, 1040f)]
    [InlineData(420f, 900f)]
    public void Replacement_preview_bounds_still_render_at_the_same_width_height_and_center(
        float visualWidth,
        float visualHeight
    )
    {
        var anchor = new CollectionInspectionCardAnchor();
        var original = anchor.Resolve(
            2560f,
            1600f,
            CollectionInspectionLayout.ForContent(2560f, 1600f, 400f, 1000f, 650f, 400f)
        );
        var actual = anchor.Resolve(
            2560f,
            1600f,
            CollectionInspectionLayout.ForContent(
                2560f,
                1600f,
                visualWidth,
                visualHeight,
                1200f,
                2600f
            )
        );
        var scale = actual.ScaleFor(visualWidth, visualHeight);
        Assert.InRange(Math.Abs(visualWidth * scale.X - original.Width), 0f, .001f);
        Assert.InRange(Math.Abs(visualHeight * scale.Y - original.Height), 0f, .001f);
        Assert.Equal(original.X + original.Width / 2f, actual.X + visualWidth * scale.X / 2f, 2);
        Assert.Equal(original.Y + original.Height / 2f, actual.Y + visualHeight * scale.Y / 2f, 2);
    }

    [Fact]
    public void Closing_or_resizing_releases_the_card_layout_anchor()
    {
        var anchor = new CollectionInspectionCardAnchor();
        var original = anchor.Resolve(
            2560f,
            1600f,
            CollectionInspectionLayout.ForContent(2560f, 1600f, 400f, 1000f, 650f, 400f)
        );
        Assert.False(anchor.NeedsLayout(2560f, 1600f));
        Assert.True(anchor.NeedsLayout(1920f, 1080f));
        var resized = CollectionInspectionLayout.ForContent(1920f, 1080f, 400f, 1000f, 650f, 1800f);
        Assert.Equal(resized.CardWidth, anchor.Resolve(1920f, 1080f, resized).Width);
        anchor.Reset();
        Assert.True(anchor.NeedsLayout(2560f, 1600f));
        var reopened = anchor.Resolve(
            2560f,
            1600f,
            CollectionInspectionLayout.ForContent(2560f, 1600f, 1200f, 1000f, 650f, 400f)
        );
        Assert.NotEqual(original, reopened);
    }

    [Fact]
    public void Primary_click_opens_only_when_released_on_the_same_card_without_dragging()
    {
        var click = new CollectionInspectionClick();
        var card = Guid.NewGuid();
        Assert.False(click.Sample(true, false, card, 100f, 100f, 0f));
        Assert.True(click.Sample(false, true, card, 102f, 100f, 0f));
        Assert.False(click.Sample(false, true, card, 102f, 100f, 0f));
        Assert.False(click.Sample(true, false, card, 100f, 100f, 0f));
        Assert.False(click.Sample(false, true, Guid.NewGuid(), 100f, 100f, 0f));
        Assert.False(click.Sample(true, false, null, 100f, 100f, 0f));
        Assert.False(click.Sample(false, true, card, 100f, 100f, 0f));
    }

    [Theory]
    [InlineData(120f, 100f, 0f)]
    [InlineData(100f, 100f, 20f)]
    public void Dragging_or_scrolling_cancels_the_primary_click_even_if_it_returns(
        float x,
        float y,
        float scroll
    )
    {
        var click = new CollectionInspectionClick();
        var card = Guid.NewGuid();
        Assert.False(click.Sample(true, false, card, 100f, 100f, 0f));
        Assert.False(click.Sample(false, false, card, x, y, scroll));
        Assert.False(click.Sample(false, true, card, 100f, 100f, 0f));
    }

    [Fact]
    public void Only_clicks_outside_variant_controls_dismiss()
    {
        var press = new InspectionBackgroundPress();
        Assert.False(press.Sample(1, false, true, false));
        Assert.False(press.Sample(2, true, false, true));
        Assert.False(press.Sample(3, false, true, false));
        Assert.False(press.Sample(4, true, false, false));
        Assert.False(press.Sample(5, false, true, true));
        Assert.False(press.Sample(6, true, false, false));
        Assert.True(press.Sample(7, false, true, false));
        Assert.False(press.Sample(7, false, true, false));
    }

    [Fact]
    public void Dropdown_controls_keep_their_click_but_the_next_outside_click_dismisses()
    {
        var press = new InspectionBackgroundPress();
        Assert.False(press.Sample(1, true, false, true));
        Assert.False(press.Sample(2, false, true, true));
        Assert.False(press.Sample(3, true, false, false));
        Assert.True(press.Sample(4, false, true, false));
    }

    [Fact]
    public void Duplicate_frames_focus_loss_and_coalesced_edges_cannot_cause_a_dismissal()
    {
        var press = new InspectionBackgroundPress();
        Assert.False(press.Sample(1, true, false, true));
        Assert.False(press.Sample(1, true, false, false));
        Assert.False(press.Sample(2, false, true, false));
        Assert.False(press.Sample(3, true, false, false));
        press.Reset();
        Assert.False(press.Sample(4, false, true, false));
        Assert.False(press.Sample(5, true, true, false));
        Assert.False(press.Sample(6, false, true, false));
    }
}
