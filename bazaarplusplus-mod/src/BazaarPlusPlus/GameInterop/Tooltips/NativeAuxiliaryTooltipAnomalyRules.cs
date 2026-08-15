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
}
