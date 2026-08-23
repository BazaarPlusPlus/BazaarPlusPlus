#nullable enable
using UnityEngine;

namespace BazaarPlusPlus.GameInterop.Tooltips;

/// <summary>
/// Which side of the primary tooltip the auxiliary panel is placed on.
/// </summary>
internal enum PairSide
{
    None,
    Right,
    Left,
}

/// <summary>
/// Why the session's TryOpen reported the native pair as unusable. The host stays log-free; the
/// consuming feature owns turning these into its own reason codes.
/// </summary>
internal enum NativePairedTooltipOpenFailure
{
    None,

    /// <summary>The auxiliary controller is missing auxParent/header/body fields.</summary>
    MissingAuxiliaryFields,

    /// <summary>Gate creation failed — the controller's Destroy is already pending.</summary>
    DyingController,

    /// <summary>The auxiliary or primary background image/sprite is unavailable.</summary>
    MissingBackground,

    /// <summary>The native background clone could not be built from the primary.</summary>
    BackgroundCloneRejected,
}

/// <summary>
/// Shared tolerance for the paired-tooltip host.
/// </summary>
/// <remarks>
/// This single constant deliberately carries two different meanings, because the code it was
/// extracted from used one value for both and the extraction must not change behavior:
/// <list type="bullet">
/// <item>a geometry tolerance in canvas units (side selection, overflow, vertical fit), and</item>
/// <item>a <see cref="CanvasGroup"/> alpha threshold — a gate counts as interactive once
/// <c>alpha &gt;= 1 - Epsilon</c>, and a hide skips the fade once <c>alpha &lt;= Epsilon</c>.</item>
/// </list>
/// That overlap is a historical fact, not a design decision. Keep both consumers pointed at this
/// one constant: splitting it into two independently-drifting values changes when a half-faded
/// tooltip becomes clickable.
/// </remarks>
internal static class NativePairedTooltipMetrics
{
    internal const float Epsilon = 0.5f;
}

/// <summary>
/// Numeric presentation parameters supplied by the consuming feature.
/// </summary>
/// <remarks>
/// These are passed in rather than defaulted inside the host so one feature's visual design does
/// not silently become the global default for the next consumer.
/// </remarks>
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

    /// <summary>Content width the panel is laid out at when space allows.</summary>
    internal float PreferredContentWidth { get; }

    /// <summary>Below this content width the placement reports <see cref="PlacementResult.WidthBelowReadable"/>.</summary>
    internal float ReadableContentWidth { get; }

    /// <summary>Horizontal gap between the primary tooltip and the auxiliary panel.</summary>
    internal float Gap { get; }

    /// <summary>Inset applied to the canvas rect before fitting.</summary>
    internal float CanvasMargin { get; }

    /// <summary>Duration of the paired reveal/hide fade, in unscaled seconds.</summary>
    internal float FadeDuration { get; }

    /// <summary>Inset removed from the native auxiliary layout's normal bottom padding.</summary>
    internal int NativeBottomPaddingReduction { get; }

    /// <summary>Bottom inset retained when a dense panel is near the canvas height limit.</summary>
    internal int NativeDenseBottomPaddingMaximum { get; }
}

/// <summary>
/// Outcome of a placement pass. The host reports; the consuming feature decides what — if
/// anything — to log about it.
/// </summary>
/// <remarks>
/// The host deliberately does not log placement degradations itself. The consumer owns its own
/// reason codes and their once-only/reset semantics, and duplicating that here would change the
/// number of emitted log records.
/// </remarks>
internal readonly struct PlacementResult
{
    internal PlacementResult(
        bool positioned,
        PairSide side,
        float contentWidth,
        bool overflowed,
        bool widthBelowReadable,
        bool topAlignmentAdjusted
    )
    {
        Positioned = positioned;
        Side = side;
        ContentWidth = contentWidth;
        Overflowed = overflowed;
        WidthBelowReadable = widthBelowReadable;
        TopAlignmentAdjusted = topAlignmentAdjusted;
    }

    /// <summary>False when the pair could not be positioned at all.</summary>
    internal bool Positioned { get; }

    internal PairSide Side { get; }

    internal float ContentWidth { get; }

    internal bool Overflowed { get; }

    internal bool WidthBelowReadable { get; }

    internal bool TopAlignmentAdjusted { get; }

    internal static PlacementResult Unplaced => new(false, PairSide.None, 0f, false, false, false);
}

/// <summary>
/// The consuming feature's content-trimming strategy, driven by the host while it fits the panel
/// to the canvas.
/// </summary>
/// <remarks>
/// The host owns native-panel measurement and layout commits. The feature owns the geometry of its
/// already-settled content tree, so it can apply the final trim prefix without asking the host to
/// rebuild after every hidden row.
/// </remarks>
internal interface IPairedContentBudget
{
    /// <summary>Make every trimmable element visible again, before a fresh fit pass.</summary>
    void RestoreAll();

    /// <summary>
    /// Read the settled content geometry and hide enough elements, in trim order, to reclaim the
    /// requested local height. Returns true when any visibility changed.
    /// </summary>
    bool ApplyHeightReduction(float requiredHeightReduction);
}
