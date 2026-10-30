using BazaarPlusPlus.Android.GameInterop;
using Xunit;

namespace AndroidHost.Tests;

/// <summary>
/// The match a game canvas gets on a screen narrower than 16:10: as wide, in canvas units, as on
/// a 16:10 screen, so no panel is laid out narrower than the game knows.
/// </summary>
public sealed class AndroidScreenFillCanvasesTests
{
    [Theory]
    [InlineData(2480, 2248)] // OPPO Find N6, inner display
    [InlineData(2176, 1812)]
    [InlineData(2560, 1920)] // 4:3
    [InlineData(2400, 1600)] // 3:2
    public void A_narrow_screen_gets_a_canvas_as_wide_as_16_10(int width, int height)
    {
        var match = AndroidScreenFillCanvases.MatchFor((float)width / height);
        var scale = Math.Exp(
            (1 - match) * Math.Log(width / 3840.0) + match * Math.Log(height / 2160.0)
        );
        Assert.InRange(match, 0f, 1f);
        Assert.Equal(2160.0 * 16 / 10, width / scale, 0);
    }

    [Fact]
    public void At_16_10_the_canvas_follows_the_height_as_the_game_set_it()
    {
        Assert.Equal(1f, AndroidScreenFillCanvases.MatchFor(16f / 10f), 3);
    }
}
