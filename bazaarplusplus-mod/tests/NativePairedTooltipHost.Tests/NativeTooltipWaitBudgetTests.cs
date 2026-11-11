using BazaarPlusPlus.GameInterop.Tooltips;
using Xunit;

namespace BazaarPlusPlus.Tests.NativePairedTooltipHost;

public sealed class NativeTooltipWaitBudgetTests
{
    private static readonly NativeTooltipWaitBudget Budget = new(frames: 60, seconds: 1f);

    // The native auxiliary fade-in measured on 1.0.13081: 0.157 to 0.166 s from the show request
    // to HasShown, at any frame rate.
    private const float NativeFadeInSeconds = 0.166f;

    [Fact]
    public void At_60_fps_the_wait_ends_at_its_frame_count()
    {
        Assert.False(Budget.IsExpired(elapsedFrames: 59, elapsedSeconds: 59 / 60f));
        Assert.True(Budget.IsExpired(elapsedFrames: 60, elapsedSeconds: 60 / 60f));
    }

    [Theory]
    [InlineData(360)]
    [InlineData(480)]
    [InlineData(1000)]
    public void At_a_high_frame_rate_the_wait_outlasts_the_native_fade_in(int framesPerSecond)
    {
        var fadeInFrames = (int)(NativeFadeInSeconds * framesPerSecond) + 1;

        Assert.False(
            Budget.IsExpired(fadeInFrames, elapsedSeconds: fadeInFrames / (float)framesPerSecond)
        );
        Assert.True(Budget.IsExpired(framesPerSecond, elapsedSeconds: 1f));
    }

    [Fact]
    public void At_a_low_frame_rate_the_wait_still_gets_its_frame_floor()
    {
        Assert.False(Budget.IsExpired(elapsedFrames: 10, elapsedSeconds: 1f));
        Assert.True(Budget.IsExpired(elapsedFrames: 60, elapsedSeconds: 6f));
    }
}
