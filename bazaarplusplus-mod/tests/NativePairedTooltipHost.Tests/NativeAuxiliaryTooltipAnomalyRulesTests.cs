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

    [Fact]
    public void Idle_visible_frame_with_active_text_nodes_proves_the_handback_healed() =>
        Assert.True(
            NativeAuxiliaryTooltipAnomalyRules.TextNodesProvenRestored(
                takeoverInProgress: false,
                frameVisible: true,
                pairedContentActive: false,
                headerActive: true,
                bodyActive: true,
                headerRenderable: true,
                bodyRenderable: false
            )
        );

    [Theory]
    [InlineData(true, true, false, true, true, true, true)]
    [InlineData(false, false, false, true, true, true, true)]
    [InlineData(false, true, true, true, true, true, true)]
    [InlineData(false, true, false, true, false, true, false)]
    [InlineData(false, true, false, false, true, false, true)]
    [InlineData(false, true, false, true, true, false, false)]
    public void Takeover_hidden_paired_or_partly_inactive_frames_keep_the_audit_armed(
        bool takeoverInProgress,
        bool frameVisible,
        bool pairedContentActive,
        bool headerActive,
        bool bodyActive,
        bool headerRenderable,
        bool bodyRenderable
    ) =>
        Assert.False(
            NativeAuxiliaryTooltipAnomalyRules.TextNodesProvenRestored(
                takeoverInProgress,
                frameVisible,
                pairedContentActive,
                headerActive,
                bodyActive,
                headerRenderable,
                bodyRenderable
            )
        );
}
