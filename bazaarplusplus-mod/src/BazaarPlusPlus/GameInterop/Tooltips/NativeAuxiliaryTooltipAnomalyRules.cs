#nullable enable

namespace BazaarPlusPlus.GameInterop.Tooltips;

/// <summary>Pure anomaly rules for the pooled native auxiliary tooltip.</summary>
internal static class NativeAuxiliaryTooltipAnomalyRules
{
    internal static bool RequestedTextNeedsRecovery(
        bool headerRequested,
        bool bodyRequested,
        bool headerActive,
        bool bodyActive
    ) => (headerRequested && !headerActive) || (bodyRequested && !bodyActive);

    internal static bool IsVisibleWithoutText(
        bool frameVisible,
        bool pairedContentActive,
        bool headerRenderable,
        bool bodyRenderable
    ) => frameVisible && !pairedContentActive && !headerRenderable && !bodyRenderable;

    /// <summary>Only a paired-tooltip takeover deactivates the pooled controller's text nodes, so
    /// the frame audit can stand down once, with no takeover in progress, a visible native frame
    /// shows both nodes active and some text renderable: no inactive-node leftover survives
    /// that, and the next takeover re-arms the audit.</summary>
    internal static bool TextNodesProvenRestored(
        bool takeoverInProgress,
        bool frameVisible,
        bool pairedContentActive,
        bool headerActive,
        bool bodyActive,
        bool headerRenderable,
        bool bodyRenderable
    ) =>
        !takeoverInProgress
        && frameVisible
        && !pairedContentActive
        && headerActive
        && bodyActive
        && (headerRenderable || bodyRenderable);
}
