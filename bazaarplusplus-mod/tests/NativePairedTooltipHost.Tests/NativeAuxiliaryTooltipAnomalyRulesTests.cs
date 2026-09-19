using BazaarPlusPlus.GameInterop.Tooltips;
using Xunit;

namespace BazaarPlusPlus.Tests.NativePairedTooltipHost;

public sealed class NativeAuxiliaryTooltipAnomalyRulesTests
{
    [Theory]
    [InlineData(true, false, false, true)]
    [InlineData(false, true, true, false)]
    [InlineData(true, true, false, false)]
    public void Requested_text_needs_recovery_when_its_node_is_inactive(
        bool headerRequested,
        bool bodyRequested,
        bool headerActive,
        bool bodyActive
    ) =>
        Assert.True(
            NativeAuxiliaryTooltipAnomalyRules.RequestedTextNeedsRecovery(
                headerRequested,
                bodyRequested,
                headerActive,
                bodyActive
            )
        );

    [Fact]
    public void Visible_frame_without_native_or_paired_text_is_an_anomaly() =>
        Assert.True(
            NativeAuxiliaryTooltipAnomalyRules.IsVisibleWithoutText(
                frameVisible: true,
                pairedContentActive: false,
                headerRenderable: false,
                bodyRenderable: false
            )
        );

    [Theory]
    [InlineData(false, false, false, false)]
    [InlineData(true, true, false, false)]
    [InlineData(true, false, true, false)]
    [InlineData(true, false, false, true)]
    public void Hidden_custom_or_text_bearing_frames_are_not_empty_frame_anomalies(
        bool frameVisible,
        bool pairedContentActive,
        bool headerRenderable,
        bool bodyRenderable
    ) =>
        Assert.False(
            NativeAuxiliaryTooltipAnomalyRules.IsVisibleWithoutText(
                frameVisible,
                pairedContentActive,
                headerRenderable,
                bodyRenderable
            )
        );
}
