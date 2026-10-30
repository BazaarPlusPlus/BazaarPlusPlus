#nullable enable
using BazaarPlusPlus.Core.Ui;
using Xunit;

namespace SettingsDockCore.Tests;

/// <summary>
/// Screen Fill's decisions on the screen shapes both touch hosts meet: 20:9 and 16:9 phones, the
/// 16:10 tablet, 3:2, a 4:3 iPad or tablet, and a foldable's nearly square inner display (OPPO
/// Find N6, 2480×2248).
/// </summary>
public sealed class ScreenFillTests
{
    [Theory]
    [InlineData(2400, 1080)] // 20:9
    [InlineData(1920, 1080)] // 16:9
    [InlineData(2560, 1600)] // 16:10
    public void A_screen_16_10_or_wider_is_left_as_the_game_draws_it(int width, int height)
    {
        var aspect = ScreenFill.Aspect(width, height);
        Assert.False(ScreenFill.Fills(aspect));
        Assert.False(ScreenFill.GathersHudAbove(aspect));
        Assert.Equal(60f, ScreenFill.WidenedFieldOfView(60f, aspect));
        Assert.Equal(0.7f, ScreenFill.GameCanvasMatch(aspect, 0.7f));
    }

    [Theory]
    [InlineData(2400, 1600, false)] // 3:2: the board keeps the 16:10 framing
    [InlineData(2560, 1920, true)] // 4:3
    [InlineData(2266, 1488, false)] // iPad mini (A17 Pro), about 1.52:1
    [InlineData(2420, 1668, false)] // iPad Pro 11-inch (M5), about 1.45:1
    [InlineData(2752, 2064, true)] // iPad Pro 13-inch (M5), 4:3
    [InlineData(2480, 2248, true)] // the foldable's inner display
    public void Only_a_screen_near_4_3_or_squarer_gathers_the_hud_above_the_board(
        int width,
        int height,
        bool above
    )
    {
        var aspect = ScreenFill.Aspect(width, height);
        Assert.True(ScreenFill.Fills(aspect));
        Assert.Equal(above, ScreenFill.GathersHudAbove(aspect));
    }

    [Fact]
    public void The_band_starts_below_about_1_36_to_1()
    {
        Assert.True(ScreenFill.GathersHudAbove(1.35f));
        Assert.False(ScreenFill.GathersHudAbove(1.37f));
        Assert.False(ScreenFill.GathersHudAbove(0f));
    }

    [Theory]
    [InlineData(2400, 1600)]
    [InlineData(2560, 1920)]
    [InlineData(2480, 2248)]
    public void A_narrow_screen_shows_at_least_the_16_10_width_of_the_board_or_a_zoom_of_it(
        int width,
        int height
    )
    {
        const float fieldOfView = 30f;
        var aspect = ScreenFill.Aspect(width, height);
        var widened = ScreenFill.WidenedFieldOfView(fieldOfView, aspect);
        var horizontal = Math.Tan(widened * Math.PI / 360) * aspect;
        var reference = Math.Tan(fieldOfView * Math.PI / 360) * ScreenFill.ReferenceAspect;
        Assert.True(widened > fieldOfView);
        if (ScreenFill.GathersHudAbove(aspect))
            // The board drawn 1.3 times nearer than 16:10 does, or less where the band needs room.
            Assert.InRange(horizontal / reference, 1 / 1.3 - 1e-4, 1.0);
        else
            // Exactly the 16:10 view's width.
            Assert.Equal(1.0, horizontal / reference, 4);
    }

    [Fact]
    public void Near_4_3_the_band_sets_the_stretch()
    {
        var aspect = 4f / 3f;
        var widened = ScreenFill.WidenedFieldOfView(30f, aspect);
        var stretch = Math.Tan(widened * Math.PI / 360) / Math.Tan(30 * Math.PI / 360);
        Assert.Equal(1.12, stretch, 4);
    }

    [Fact]
    public void The_board_drops_to_a_margin_above_the_safe_bottom()
    {
        // 1536 px tall, 20 px unsafe at the bottom: the floor is 1536 - 20 - 18.4 down from the top.
        var drop = ScreenFill.BoardDrop(1536f, 20f, vaultBottomFromTop: 1300f);
        Assert.Equal(1536f - 20f - 1536f * 0.012f - 1300f, drop, 3);
        Assert.True(ScreenFill.BoardDrop(1536f, 0f, vaultBottomFromTop: 1530f) < 0f);
    }

    [Fact]
    public void A_pixel_spans_the_view_height_over_the_screen_height()
    {
        // A 90 degree view at depth 10 is 20 units tall.
        Assert.Equal(20f / 1000f, ScreenFill.WorldUnitsPerPixel(10f, 90f, 1000f), 5);
        Assert.Equal(0f, ScreenFill.WorldUnitsPerPixel(10f, 90f, 0f));
    }

    [Fact]
    public void The_board_corner_mirrors_the_vault_through_its_centre()
    {
        var (x, y) = ScreenFill.BoardTopLeft(
            1000f,
            700f,
            vaultRight: 1700f,
            vaultBottomFromTop: 1300f
        );
        Assert.Equal(300f, x);
        Assert.Equal(100f, y);
    }

    [Fact]
    public void The_day_clock_shrinks_into_the_band_left_aligned_with_the_board()
    {
        // 1536 px tall; the board's top 300 px down, the safe top at the screen top: a 300 px band.
        var clock = new ScreenBox(XMin: 40f, YMin: 1100f, Width: 200f, Height: 250f);
        var placement = ScreenFill.PlaceDayClock(
            1536f,
            safeAreaXMin: 0f,
            safeAreaYMax: 1536f,
            boardLeft: 400f,
            boardTopFromTop: 300f,
            clock,
            pivotX: clock.CenterX,
            pivotY: clock.CenterY
        );
        Assert.NotNull(placement);
        // 0.8 of its size fits the 76% of the band left between the margins.
        Assert.Equal(0.8f, placement!.Value.Scale, 4);
        Assert.Equal(400f + 200f * 0.8f / 2f, placement.Value.PivotX, 3);
        Assert.Equal(1536f - 300f + 150f, placement.Value.PivotY, 3);
    }

    [Fact]
    public void A_band_too_short_leaves_the_clock_where_the_game_put_it()
    {
        var clock = new ScreenBox(40f, 1100f, 200f, 250f);
        Assert.Null(ScreenFill.PlaceDayClock(1536f, 0f, 1536f, 400f, 100f, clock, 140f, 1225f));
    }

    [Theory]
    [InlineData(2480, 2248)] // OPPO Find N6, inner display
    [InlineData(2176, 1812)]
    [InlineData(2560, 1920)] // 4:3
    [InlineData(2400, 1600)] // 3:2
    public void A_narrow_screen_gets_a_canvas_as_wide_as_16_10(int width, int height)
    {
        var match = ScreenFill.CanvasMatch((float)width / height);
        var scale = Math.Exp(
            (1 - match) * Math.Log(width / 3840.0) + match * Math.Log(height / 2160.0)
        );
        Assert.InRange(match, 0f, 1f);
        Assert.Equal(2160.0 * 16 / 10, width / scale, 0);
    }

    [Fact]
    public void At_16_10_the_canvas_follows_the_height_as_the_game_set_it()
    {
        Assert.Equal(1f, ScreenFill.CanvasMatch(16f / 10f), 3);
    }

    [Theory]
    [InlineData(3840f, 2160f, 1f, "MainCanvas", true)]
    [InlineData(3840f, 2160f, 1f, "Tooltip_Mobile_P", false)]
    [InlineData(3840f, 2160f, 0.5f, "MainCanvas", false)]
    [InlineData(1920f, 1080f, 1f, "BPP.Overlay", false)]
    public void Screen_fill_takes_only_the_game_canvases_that_follow_the_height(
        float width,
        float height,
        float match,
        string name,
        bool taken
    ) => Assert.Equal(taken, ScreenFill.TakesCanvas(width, height, match, name));
}
