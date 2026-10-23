using BazaarPlusPlus.Game.PvpBattles;
using Xunit;

namespace AndroidHost.Tests;

/// <summary>
/// The label rule both hosts store a board capture with, on the net10.0 Core the Android host
/// ships. An empty opening read is retried live instead of standing as a definite zero.
/// </summary>
public sealed class PvpBattleCaptureRuleTests
{
    [Theory]
    [InlineData(true, false, 0, true)]
    [InlineData(false, false, 0, true)]
    [InlineData(false, false, 3, true)]
    [InlineData(true, false, 2, false)]
    [InlineData(true, true, 0, false)]
    [InlineData(false, true, 0, false)]
    public void A_player_set_is_retried_live_until_one_live_read_succeeds(
        bool capturedFromOpening,
        bool capturedLive,
        int itemCount,
        bool retried
    )
    {
        Assert.Equal(
            retried,
            PvpBattleCaptureRule.ShouldRetryLive(capturedFromOpening, capturedLive, itemCount)
        );
    }

    [Theory]
    [InlineData(false, false, 0, PvpBattleCaptureStatus.Missing, PvpBattleCaptureSource.Unknown)]
    [InlineData(
        true,
        false,
        0,
        PvpBattleCaptureStatus.CapturedEmpty,
        PvpBattleCaptureSource.OpeningMessage
    )]
    [InlineData(
        true,
        false,
        2,
        PvpBattleCaptureStatus.Captured,
        PvpBattleCaptureSource.OpeningMessage
    )]
    [InlineData(false, true, 1, PvpBattleCaptureStatus.Captured, PvpBattleCaptureSource.LiveRetry)]
    [InlineData(
        false,
        true,
        0,
        PvpBattleCaptureStatus.CapturedEmpty,
        PvpBattleCaptureSource.LiveRetry
    )]
    // An empty opening read that the live retry filled keeps the opening label, as the desktop
    // has always stored it.
    [InlineData(
        true,
        true,
        2,
        PvpBattleCaptureStatus.Captured,
        PvpBattleCaptureSource.OpeningMessage
    )]
    public void A_set_is_labelled_by_the_read_that_captured_it(
        bool capturedFromOpening,
        bool capturedLive,
        int itemCount,
        PvpBattleCaptureStatus status,
        PvpBattleCaptureSource source
    )
    {
        Assert.Equal(
            (status, source),
            PvpBattleCaptureRule.Label(capturedFromOpening, capturedLive, itemCount)
        );
    }
}
