#nullable enable
using UnityEngine;

namespace BazaarPlusPlus.GameInterop.Tooltips;

/// <summary>
/// The purely geometric decisions behind paired-tooltip placement.
/// </summary>
/// <remarks>
/// Every method here is a pure function of the rects and scalars it is given — nothing reads a
/// live <see cref="RectTransform"/>, <see cref="Canvas"/>, or any other Unity runtime state — so
/// the placement rules are unit-testable without starting the game.
/// <para>
/// The formulas and the <see cref="NativePairedTooltipMetrics.Epsilon"/> tolerance are preserved
/// verbatim from the pre-extraction implementation. This file intentionally keeps its
/// <c>UnityEngine</c> dependency for <see cref="Rect"/>/<see cref="Vector2"/>/<see cref="Mathf"/>
/// rather than restating the maths over bespoke primitives: rewriting the expressions would put
/// the "behavior is unchanged" guarantee at risk for no test-harness benefit.
/// </para>
/// </remarks>
internal static class NativePairedTooltipPlacementMath
{
    /// <summary>
    /// Picks the side for the auxiliary panel: prefer the right when the preferred width fits,
    /// then the left, otherwise whichever side has more room (ties keep the right).
    /// </summary>
    internal static PairSide ChooseSide(
        float availableRight,
        float availableLeft,
        float preferredFrameWidth
    )
    {
        if (availableRight + NativePairedTooltipMetrics.Epsilon >= preferredFrameWidth)
            return PairSide.Right;
        if (availableLeft + NativePairedTooltipMetrics.Epsilon >= preferredFrameWidth)
            return PairSide.Left;
        return availableRight >= availableLeft ? PairSide.Right : PairSide.Left;
    }

    /// <summary>Horizontal room to the right of the primary tooltip, inside the canvas bounds.</summary>
    internal static float AvailableRight(Rect canvasBounds, Rect primaryBounds, float gap) =>
        Mathf.Max(0f, canvasBounds.xMax - (primaryBounds.xMax + gap));

    /// <summary>Horizontal room to the left of the primary tooltip, inside the canvas bounds.</summary>
    internal static float AvailableLeft(Rect canvasBounds, Rect primaryBounds, float gap) =>
        Mathf.Max(0f, primaryBounds.xMin - gap - canvasBounds.xMin);

    /// <summary>
    /// Content width that fits on <paramref name="side"/>, in the panel's own local units.
    /// </summary>
    internal static float ResolveContentWidth(
        PairSide side,
        float availableRight,
        float availableLeft,
        float canvasUnitsPerLocalUnit,
        float frameHorizontalBleed,
        float preferredContentWidth
    )
    {
        var available = side == PairSide.Right ? availableRight : availableLeft;
        var availableContentWidth = available / canvasUnitsPerLocalUnit - frameHorizontalBleed;
        return Mathf.Min(preferredContentWidth, Mathf.Max(1f, availableContentWidth));
    }

    /// <summary>
    /// Offset that moves the panel beside the primary tooltip and top-aligns the two.
    /// </summary>
    internal static Vector2 ResolvePairOffset(
        PairSide side,
        Rect primaryBounds,
        Rect panelBounds,
        float gap
    ) =>
        side == PairSide.Right
            ? new Vector2(
                primaryBounds.xMax + gap - panelBounds.xMin,
                primaryBounds.yMax - panelBounds.yMax
            )
            : new Vector2(
                primaryBounds.xMin - gap - panelBounds.xMax,
                primaryBounds.yMax - panelBounds.yMax
            );

    /// <summary>
    /// Vertical correction that pulls <paramref name="rect"/> back inside <paramref name="bounds"/>.
    /// A rect taller than the bounds is top-aligned rather than centered.
    /// </summary>
    internal static float ResolveVerticalAdjustment(Rect rect, Rect bounds)
    {
        if (rect.height > bounds.height + NativePairedTooltipMetrics.Epsilon)
            return bounds.yMax - rect.yMax;

        var adjustment = rect.yMax > bounds.yMax ? bounds.yMax - rect.yMax : 0f;
        if (rect.yMin + adjustment < bounds.yMin)
            adjustment += bounds.yMin - (rect.yMin + adjustment);
        return adjustment;
    }

    /// <summary>
    /// True when the panel has crept back into the primary tooltip's gap on the chosen side.
    /// </summary>
    internal static bool Collides(PairSide side, Rect primaryBounds, Rect panelBounds, float gap) =>
        side == PairSide.Right
            ? panelBounds.xMin < primaryBounds.xMax + gap - NativePairedTooltipMetrics.Epsilon
            : panelBounds.xMax > primaryBounds.xMin - gap + NativePairedTooltipMetrics.Epsilon;

    /// <summary>
    /// True when the primary/panel pair does not fit the screen, or the two overlap.
    /// </summary>
    internal static bool Overflows(
        PairSide side,
        Rect primaryBounds,
        Rect panelBounds,
        Rect screenBounds,
        float gap
    )
    {
        var pairBounds = Union(primaryBounds, panelBounds);
        return pairBounds.width > screenBounds.width
            || pairBounds.height > screenBounds.height
            || pairBounds.xMin < screenBounds.xMin - NativePairedTooltipMetrics.Epsilon
            || pairBounds.xMax > screenBounds.xMax + NativePairedTooltipMetrics.Epsilon
            || pairBounds.yMin < screenBounds.yMin - NativePairedTooltipMetrics.Epsilon
            || pairBounds.yMax > screenBounds.yMax + NativePairedTooltipMetrics.Epsilon
            || Collides(side, primaryBounds, panelBounds, gap);
    }

    /// <summary>Smallest rect containing both inputs.</summary>
    internal static Rect Union(Rect first, Rect second) =>
        Rect.MinMaxRect(
            Mathf.Min(first.xMin, second.xMin),
            Mathf.Min(first.yMin, second.yMin),
            Mathf.Max(first.xMax, second.xMax),
            Mathf.Max(first.yMax, second.yMax)
        );

    /// <summary>Shrinks a rect on all sides, never past its own center.</summary>
    internal static Rect Inset(Rect rect, float inset)
    {
        var horizontalInset = Mathf.Min(inset, rect.width * 0.5f);
        var verticalInset = Mathf.Min(inset, rect.height * 0.5f);
        return Rect.MinMaxRect(
            rect.xMin + horizontalInset,
            rect.yMin + verticalInset,
            rect.xMax - horizontalInset,
            rect.yMax - verticalInset
        );
    }

    /// <summary>
    /// Whether reclaiming the optional bottom inset would materially help a panel near the canvas
    /// height limit. All inputs use the same coordinate space.
    /// </summary>
    internal static bool ShouldUseDenseBottomPadding(
        float panelHeight,
        float availableHeight,
        float normalBottomPadding,
        float denseBottomPadding
    )
    {
        var reclaimablePadding = Mathf.Max(0f, normalBottomPadding - denseBottomPadding);
        return reclaimablePadding > NativePairedTooltipMetrics.Epsilon
            && panelHeight
                > availableHeight - reclaimablePadding + NativePairedTooltipMetrics.Epsilon;
    }
}
