#nullable enable

namespace BazaarPlusPlus.GameInterop.Tooltips;

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

    // A paired takeover is the only writer that deactivates these text nodes. Once both
    // render natively, the audit can stand down until the next takeover.
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
