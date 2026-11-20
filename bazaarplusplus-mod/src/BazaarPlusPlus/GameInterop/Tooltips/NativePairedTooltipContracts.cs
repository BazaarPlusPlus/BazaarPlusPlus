#nullable enable
using UnityEngine;

namespace BazaarPlusPlus.GameInterop.Tooltips;

internal enum PairSide
{
    None,
    Right,
    Left,
}

internal enum NativePairedTooltipOpenFailure
{
    None,

    MissingAuxiliaryFields,

    DyingController,

    MissingBackground,

    BackgroundCloneRejected,
}

// Changing the geometry tolerance also changes when a fading gate becomes interactive.
internal static class NativePairedTooltipMetrics
{
    internal const float Epsilon = 0.5f;
}

internal readonly struct NativePairedTooltipOptions
{
    internal NativePairedTooltipOptions(
        float preferredContentWidth,
        float readableContentWidth,
        float gap,
        float canvasMargin,
        float fadeDuration,
        int nativeBottomPaddingReduction,
        int nativeDenseBottomPaddingMaximum
    )
    {
        PreferredContentWidth = preferredContentWidth;
        ReadableContentWidth = readableContentWidth;
        Gap = gap;
        CanvasMargin = canvasMargin;
        FadeDuration = fadeDuration;
        NativeBottomPaddingReduction = nativeBottomPaddingReduction;
        NativeDenseBottomPaddingMaximum = Mathf.Max(0, nativeDenseBottomPaddingMaximum);
    }

    internal float PreferredContentWidth { get; }

    internal float ReadableContentWidth { get; }

    internal float Gap { get; }

    internal float CanvasMargin { get; }

    /// <summary>Duration of the paired reveal/hide fade, in unscaled seconds.</summary>
    internal float FadeDuration { get; }

    internal int NativeBottomPaddingReduction { get; }

    internal int NativeDenseBottomPaddingMaximum { get; }
}

internal readonly struct PlacementResult
{
    internal PlacementResult(
        bool positioned,
        PairSide side,
        bool overflowed,
        bool widthBelowReadable,
        bool topAlignmentAdjusted
    )
    {
        Positioned = positioned;
        Side = side;
        Overflowed = overflowed;
        WidthBelowReadable = widthBelowReadable;
        TopAlignmentAdjusted = topAlignmentAdjusted;
    }

    internal bool Positioned { get; }

    internal PairSide Side { get; }

    internal bool Overflowed { get; }

    internal bool WidthBelowReadable { get; }

    internal bool TopAlignmentAdjusted { get; }

    internal static PlacementResult Unplaced => new(false, PairSide.None, false, false, false);
}

// The host passes local overflow from the settled layout so consumers can trim all
// necessary rows without rebuilding after each one.
internal interface IPairedContentBudget
{
    void RestoreAll();

    bool ApplyHeightReduction(float requiredHeightReduction);
}
