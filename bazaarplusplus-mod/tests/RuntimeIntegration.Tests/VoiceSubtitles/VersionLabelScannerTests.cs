using BazaarPlusPlus.Game.VoiceSubtitles;
using Xunit;

namespace VoiceSubtitles.Tests;

public sealed class VersionLabelScannerTests
{
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    public void Mount_is_delayed_only_while_owned_typography_is_waiting(
        bool anchorHasChineseCoverage,
        bool nativeTypographyWaiting,
        bool expected
    )
    {
        Assert.Equal(
            expected,
            VersionLabelScanner.ShouldDelayMount(anchorHasChineseCoverage, nativeTypographyWaiting)
        );
    }

    [Fact]
    public void Mount_backoff_grows_to_the_backoff_interval_and_never_stops_retrying()
    {
        var backoff = new VersionLabelMountBackoff();
        var now = 10f;
        Assert.True(backoff.CanAttempt(now));

        for (var miss = 1; miss < VersionLabelMountBackoff.MissesBeforeBackoff; miss++)
        {
            backoff.RecordMiss(now);
            Assert.False(
                backoff.CanAttempt(now + VersionLabelMountBackoff.ScanIntervalSeconds - 0.01f)
            );
            now += VersionLabelMountBackoff.ScanIntervalSeconds;
            Assert.True(backoff.CanAttempt(now));
        }

        for (var miss = 0; miss < 5; miss++)
        {
            backoff.RecordMiss(now);
            Assert.False(backoff.CanAttempt(now + VersionLabelMountBackoff.ScanIntervalSeconds));
            Assert.False(
                backoff.CanAttempt(now + VersionLabelMountBackoff.BackoffIntervalSeconds - 0.01f)
            );
            now += VersionLabelMountBackoff.BackoffIntervalSeconds;
            Assert.True(backoff.CanAttempt(now));
        }
    }

    [Fact]
    public void Mount_backoff_reset_allows_an_immediate_attempt_and_restarts_the_miss_count()
    {
        var backoff = new VersionLabelMountBackoff();
        for (var miss = 0; miss < VersionLabelMountBackoff.MissesBeforeBackoff; miss++)
            backoff.RecordMiss(0f);
        Assert.False(backoff.CanAttempt(1f));

        backoff.Reset();
        Assert.True(backoff.CanAttempt(1f));

        backoff.RecordMiss(1f);
        Assert.True(backoff.CanAttempt(1f + VersionLabelMountBackoff.ScanIntervalSeconds));
    }
}
