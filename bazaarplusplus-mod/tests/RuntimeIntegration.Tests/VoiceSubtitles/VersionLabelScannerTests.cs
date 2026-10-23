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
}
