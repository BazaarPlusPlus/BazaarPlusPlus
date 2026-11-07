using BazaarPlusPlus.Game.HeroPortraits;
using Xunit;

namespace PureBehavior.Tests;

public sealed class PortraitPreparationSequenceTests
{
    [Fact]
    public async Task Match_and_breakout_each_complete_before_their_current_check()
    {
        var steps = new List<string>();
        var result = await PortraitPreparationSequence.RunAsync(
            () =>
            {
                steps.Add("match");
                return Task.FromResult<string?>("fit");
            },
            () =>
            {
                steps.Add("frame");
                return "opening";
            },
            (fit, frame) =>
            {
                Assert.Equal("fit", fit);
                Assert.Equal("opening", frame);
                steps.Add("breakout");
                return Task.FromResult("plan");
            },
            () => steps.Add("current")
        );

        Assert.Equal(new[] { "match", "current", "frame", "breakout", "current" }, steps);
        Assert.Equal("plan", result?.Plan);
    }

    [Fact]
    public async Task Missing_match_checks_current_then_skips_frame_and_breakout()
    {
        var steps = new List<string>();
        var result = await PortraitPreparationSequence.RunAsync<string, string, string>(
            () => Task.FromResult<string?>(null),
            () => throw new InvalidOperationException("frame reached"),
            (_, _) => throw new InvalidOperationException("breakout reached"),
            () => steps.Add("current")
        );

        Assert.Null(result);
        Assert.Equal(new[] { "current" }, steps);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Superseded_portrait_stops_after_the_completed_phase(int invalidAtCheck)
    {
        var checks = 0;
        var breakoutCalls = 0;
        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            await PortraitPreparationSequence.RunAsync(
                () => Task.FromResult<string?>("fit"),
                () => "opening",
                (_, _) =>
                {
                    breakoutCalls++;
                    return Task.FromResult("plan");
                },
                () =>
                {
                    if (++checks == invalidAtCheck)
                        throw new OperationCanceledException();
                }
            );
        });

        Assert.Equal(invalidAtCheck == 1 ? 0 : 1, breakoutCalls);
    }

    [Fact]
    public async Task Supersession_during_match_skips_frame_and_breakout()
    {
        var match = new TaskCompletionSource<string?>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var stale = false;
        var frameReads = 0;
        var breakoutCalls = 0;
        var preparation = PortraitPreparationSequence.RunAsync(
            () => match.Task,
            () =>
            {
                frameReads++;
                return "frame";
            },
            (_, _) =>
            {
                breakoutCalls++;
                return Task.FromResult("plan");
            },
            () =>
            {
                if (stale)
                    throw new OperationCanceledException();
            }
        );

        Assert.False(preparation.IsCompleted);
        stale = true;
        match.SetResult("fit");

        await Assert.ThrowsAsync<OperationCanceledException>(() => preparation);
        Assert.Equal(0, frameReads);
        Assert.Equal(0, breakoutCalls);
    }

    [Fact]
    public async Task Supersession_during_breakout_discards_result_and_keeps_captured_images()
    {
        var breakout = new TaskCompletionSource<string>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var capturedImages = new object();
        object? handedOffImages = null;
        object? stagedImages = null;
        var stale = false;
        var checks = 0;
        var preparation = PortraitPreparationSequence.RunAsync(
            () =>
            {
                stagedImages = capturedImages;
                return Task.FromResult<string?>("fit");
            },
            () => "frame",
            (_, _) =>
            {
                handedOffImages = stagedImages;
                return breakout.Task;
            },
            () =>
            {
                checks++;
                if (stale)
                    throw new OperationCanceledException();
            }
        );

        Assert.False(preparation.IsCompleted);
        Assert.Same(capturedImages, handedOffImages);
        stale = true;
        breakout.SetResult("plan");

        await Assert.ThrowsAsync<OperationCanceledException>(() => preparation);
        Assert.Equal(2, checks);
    }
}
