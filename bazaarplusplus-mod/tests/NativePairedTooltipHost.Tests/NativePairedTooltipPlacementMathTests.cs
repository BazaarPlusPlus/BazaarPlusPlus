using BazaarPlusPlus.GameInterop.Tooltips;
using UnityEngine;
using Xunit;

namespace BazaarPlusPlus.Tests.NativePairedTooltipHost;

/// <summary>
/// Locks the placement rules that moved out of the Combat Impact tooltip view.
/// </summary>
/// <remarks>
/// These assert the pre-extraction behavior, including the deliberate right-side tie-break and the
/// shared 0.5 epsilon. A change here is a change to what the user sees.
/// </remarks>
public class NativePairedTooltipPlacementMathTests
{
    private const float Epsilon = NativePairedTooltipMetrics.Epsilon;

    [Theory]
    [InlineData(600f, 1000f, 40f, 24f, false)]
    [InlineData(985f, 1000f, 40f, 24f, true)]
    [InlineData(1001f, 1000f, 40f, 24f, true)]
    [InlineData(999f, 1000f, 20f, 24f, false)]
    public void Dense_bottom_padding_is_used_only_when_its_reclaimable_space_matters(
        float panelHeight,
        float availableHeight,
        float normalBottomPadding,
        float denseBottomPadding,
        bool expected
    )
    {
        Assert.Equal(
            expected,
            NativePairedTooltipPlacementMath.ShouldUseDenseBottomPadding(
                panelHeight,
                availableHeight,
                normalBottomPadding,
                denseBottomPadding
            )
        );
    }

    // ── Side selection ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void Prefers_the_right_side_when_the_preferred_width_fits_there()
    {
        var side = NativePairedTooltipPlacementMath.ChooseSide(
            availableRight: 700f,
            availableLeft: 700f,
            preferredFrameWidth: 660f
        );

        Assert.Equal(PairSide.Right, side);
    }

    [Fact]
    public void Falls_back_to_the_left_side_when_only_the_left_fits()
    {
        var side = NativePairedTooltipPlacementMath.ChooseSide(
            availableRight: 100f,
            availableLeft: 700f,
            preferredFrameWidth: 660f
        );

        Assert.Equal(PairSide.Left, side);
    }

    [Fact]
    public void Picks_the_roomier_side_when_neither_fits()
    {
        var side = NativePairedTooltipPlacementMath.ChooseSide(
            availableRight: 200f,
            availableLeft: 300f,
            preferredFrameWidth: 660f
        );

        Assert.Equal(PairSide.Left, side);
    }

    [Fact]
    public void Keeps_the_right_side_when_neither_fits_and_the_room_is_equal()
    {
        var side = NativePairedTooltipPlacementMath.ChooseSide(
            availableRight: 300f,
            availableLeft: 300f,
            preferredFrameWidth: 660f
        );

        Assert.Equal(PairSide.Right, side);
    }

    [Fact]
    public void Accepts_a_side_that_is_short_by_less_than_epsilon()
    {
        // The epsilon is applied to the fit test, not just to the comparisons downstream.
        var side = NativePairedTooltipPlacementMath.ChooseSide(
            availableRight: 660f - (Epsilon * 0.5f),
            availableLeft: 5000f,
            preferredFrameWidth: 660f
        );

        Assert.Equal(PairSide.Right, side);
    }

    // ── Available room ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void Available_room_never_goes_negative()
    {
        var canvasBounds = Rect.MinMaxRect(-100f, -100f, 100f, 100f);
        var primaryBounds = Rect.MinMaxRect(-200f, -50f, 200f, 50f);

        Assert.Equal(
            0f,
            NativePairedTooltipPlacementMath.AvailableRight(canvasBounds, primaryBounds, 18f)
        );
        Assert.Equal(
            0f,
            NativePairedTooltipPlacementMath.AvailableLeft(canvasBounds, primaryBounds, 18f)
        );
    }

    [Fact]
    public void Available_room_subtracts_the_gap_on_both_sides()
    {
        var canvasBounds = Rect.MinMaxRect(-500f, -300f, 500f, 300f);
        var primaryBounds = Rect.MinMaxRect(-100f, -150f, 100f, 150f);

        Assert.Equal(
            382f,
            NativePairedTooltipPlacementMath.AvailableRight(canvasBounds, primaryBounds, 18f),
            3
        );
        Assert.Equal(
            382f,
            NativePairedTooltipPlacementMath.AvailableLeft(canvasBounds, primaryBounds, 18f),
            3
        );
    }

    // ── Width clamping ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void Content_width_is_capped_at_the_preferred_width()
    {
        var width = NativePairedTooltipPlacementMath.ResolveContentWidth(
            PairSide.Right,
            availableRight: 5000f,
            availableLeft: 0f,
            canvasUnitsPerLocalUnit: 1f,
            frameHorizontalBleed: 0f,
            preferredContentWidth: 660f
        );

        Assert.Equal(660f, width, 3);
    }

    [Fact]
    public void Content_width_never_drops_below_one()
    {
        var width = NativePairedTooltipPlacementMath.ResolveContentWidth(
            PairSide.Left,
            availableRight: 0f,
            availableLeft: 0f,
            canvasUnitsPerLocalUnit: 1f,
            frameHorizontalBleed: 40f,
            preferredContentWidth: 660f
        );

        Assert.Equal(1f, width, 3);
    }

    [Fact]
    public void Content_width_discounts_the_frame_bleed_and_the_canvas_scale()
    {
        var width = NativePairedTooltipPlacementMath.ResolveContentWidth(
            PairSide.Right,
            availableRight: 400f,
            availableLeft: 0f,
            canvasUnitsPerLocalUnit: 2f,
            frameHorizontalBleed: 40f,
            preferredContentWidth: 660f
        );

        // 400 / 2 - 40
        Assert.Equal(160f, width, 3);
    }

    [Fact]
    public void Content_width_reads_the_side_it_is_given()
    {
        var width = NativePairedTooltipPlacementMath.ResolveContentWidth(
            PairSide.Left,
            availableRight: 5000f,
            availableLeft: 200f,
            canvasUnitsPerLocalUnit: 1f,
            frameHorizontalBleed: 0f,
            preferredContentWidth: 660f
        );

        Assert.Equal(200f, width, 3);
    }

    // ── Pair offset ────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Right_placement_puts_the_panel_one_gap_past_the_primary_and_tops_align()
    {
        var primary = Rect.MinMaxRect(0f, 0f, 100f, 200f);
        var panel = Rect.MinMaxRect(0f, 0f, 300f, 150f);

        var offset = NativePairedTooltipPlacementMath.ResolvePairOffset(
            PairSide.Right,
            primary,
            panel,
            gap: 18f
        );

        Assert.Equal(118f, offset.x, 3);
        Assert.Equal(50f, offset.y, 3);
    }

    [Fact]
    public void Left_placement_puts_the_panel_one_gap_before_the_primary()
    {
        var primary = Rect.MinMaxRect(0f, 0f, 100f, 200f);
        var panel = Rect.MinMaxRect(0f, 0f, 300f, 150f);

        var offset = NativePairedTooltipPlacementMath.ResolvePairOffset(
            PairSide.Left,
            primary,
            panel,
            gap: 18f
        );

        Assert.Equal(-318f, offset.x, 3);
        Assert.Equal(50f, offset.y, 3);
    }

    // ── Vertical fit ───────────────────────────────────────────────────────────────────────

    [Fact]
    public void A_rect_already_inside_the_bounds_is_not_moved()
    {
        var rect = Rect.MinMaxRect(0f, 10f, 100f, 90f);
        var bounds = Rect.MinMaxRect(0f, 0f, 100f, 100f);

        Assert.Equal(
            0f,
            NativePairedTooltipPlacementMath.ResolveVerticalAdjustment(rect, bounds),
            3
        );
    }

    [Fact]
    public void A_rect_past_the_top_is_pulled_down()
    {
        var rect = Rect.MinMaxRect(0f, 40f, 100f, 130f);
        var bounds = Rect.MinMaxRect(0f, 0f, 100f, 100f);

        Assert.Equal(
            -30f,
            NativePairedTooltipPlacementMath.ResolveVerticalAdjustment(rect, bounds),
            3
        );
    }

    [Fact]
    public void A_rect_past_the_bottom_is_pushed_up()
    {
        var rect = Rect.MinMaxRect(0f, -30f, 100f, 60f);
        var bounds = Rect.MinMaxRect(0f, 0f, 100f, 100f);

        Assert.Equal(
            30f,
            NativePairedTooltipPlacementMath.ResolveVerticalAdjustment(rect, bounds),
            3
        );
    }

    [Fact]
    public void A_rect_taller_than_the_bounds_is_top_aligned_rather_than_centered()
    {
        var rect = Rect.MinMaxRect(0f, -100f, 100f, 150f);
        var bounds = Rect.MinMaxRect(0f, 0f, 100f, 100f);

        // Top-align: yMax 150 -> 100. Bottom is left overflowing on purpose.
        Assert.Equal(
            -50f,
            NativePairedTooltipPlacementMath.ResolveVerticalAdjustment(rect, bounds),
            3
        );
    }

    // ── Overflow / collision diagnostics ───────────────────────────────────────────────────

    [Fact]
    public void A_pair_that_fits_the_screen_reports_no_overflow()
    {
        var primary = Rect.MinMaxRect(-200f, -100f, -50f, 100f);
        var panel = Rect.MinMaxRect(-32f, -100f, 200f, 100f);
        var screen = Rect.MinMaxRect(-300f, -200f, 300f, 200f);

        Assert.False(
            NativePairedTooltipPlacementMath.Overflows(
                PairSide.Right,
                primary,
                panel,
                screen,
                gap: 18f
            )
        );
    }

    [Fact]
    public void A_panel_that_crept_into_the_gap_reports_a_collision()
    {
        var primary = Rect.MinMaxRect(-200f, -100f, -50f, 100f);
        var panel = Rect.MinMaxRect(-45f, -100f, 200f, 100f);
        var screen = Rect.MinMaxRect(-300f, -200f, 300f, 200f);

        Assert.True(
            NativePairedTooltipPlacementMath.Collides(PairSide.Right, primary, panel, gap: 18f)
        );
        Assert.True(
            NativePairedTooltipPlacementMath.Overflows(
                PairSide.Right,
                primary,
                panel,
                screen,
                gap: 18f
            )
        );
    }

    [Fact]
    public void A_pair_wider_than_the_screen_reports_overflow()
    {
        var primary = Rect.MinMaxRect(-400f, -100f, -50f, 100f);
        var panel = Rect.MinMaxRect(-32f, -100f, 400f, 100f);
        var screen = Rect.MinMaxRect(-300f, -200f, 300f, 200f);

        Assert.True(
            NativePairedTooltipPlacementMath.Overflows(
                PairSide.Right,
                primary,
                panel,
                screen,
                gap: 18f
            )
        );
    }

    [Fact]
    public void A_pair_taller_than_the_screen_reports_overflow()
    {
        var primary = Rect.MinMaxRect(-200f, -300f, -50f, 300f);
        var panel = Rect.MinMaxRect(-32f, -100f, 200f, 100f);
        var screen = Rect.MinMaxRect(-300f, -200f, 300f, 200f);

        Assert.True(
            NativePairedTooltipPlacementMath.Overflows(
                PairSide.Right,
                primary,
                panel,
                screen,
                gap: 18f
            )
        );
    }

    [Fact]
    public void Left_side_collision_is_measured_against_the_primarys_left_edge()
    {
        var primary = Rect.MinMaxRect(50f, -100f, 200f, 100f);
        var panel = Rect.MinMaxRect(-200f, -100f, 45f, 100f);

        Assert.True(
            NativePairedTooltipPlacementMath.Collides(PairSide.Left, primary, panel, gap: 18f)
        );

        var clearedPanel = Rect.MinMaxRect(-200f, -100f, 30f, 100f);
        Assert.False(
            NativePairedTooltipPlacementMath.Collides(
                PairSide.Left,
                primary,
                clearedPanel,
                gap: 18f
            )
        );
    }

    // ── Rect helpers ───────────────────────────────────────────────────────────────────────

    [Fact]
    public void Union_covers_both_rects()
    {
        var union = NativePairedTooltipPlacementMath.Union(
            Rect.MinMaxRect(-10f, -20f, 5f, 5f),
            Rect.MinMaxRect(0f, 0f, 30f, 40f)
        );

        Assert.Equal(-10f, union.xMin, 3);
        Assert.Equal(-20f, union.yMin, 3);
        Assert.Equal(30f, union.xMax, 3);
        Assert.Equal(40f, union.yMax, 3);
    }

    [Fact]
    public void Inset_shrinks_on_every_side()
    {
        var inset = NativePairedTooltipPlacementMath.Inset(
            Rect.MinMaxRect(0f, 0f, 100f, 100f),
            16f
        );

        Assert.Equal(16f, inset.xMin, 3);
        Assert.Equal(16f, inset.yMin, 3);
        Assert.Equal(84f, inset.xMax, 3);
        Assert.Equal(84f, inset.yMax, 3);
    }

    [Fact]
    public void Inset_never_collapses_a_rect_past_its_own_centre()
    {
        var inset = NativePairedTooltipPlacementMath.Inset(Rect.MinMaxRect(0f, 0f, 10f, 4f), 16f);

        Assert.Equal(5f, inset.xMin, 3);
        Assert.Equal(5f, inset.xMax, 3);
        Assert.Equal(2f, inset.yMin, 3);
        Assert.Equal(2f, inset.yMax, 3);
    }
}
