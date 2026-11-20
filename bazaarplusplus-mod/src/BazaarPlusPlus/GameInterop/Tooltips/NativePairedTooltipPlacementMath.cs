#nullable enable
using UnityEngine;

namespace BazaarPlusPlus.GameInterop.Tooltips;

internal static class NativePairedTooltipPlacementMath
{
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

    internal static float AvailableRight(Rect canvasBounds, Rect primaryBounds, float gap) =>
        Mathf.Max(0f, canvasBounds.xMax - (primaryBounds.xMax + gap));

    internal static float AvailableLeft(Rect canvasBounds, Rect primaryBounds, float gap) =>
        Mathf.Max(0f, primaryBounds.xMin - gap - canvasBounds.xMin);

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

    internal static float ResolveVerticalAdjustment(Rect rect, Rect bounds)
    {
        if (rect.height > bounds.height + NativePairedTooltipMetrics.Epsilon)
            return bounds.yMax - rect.yMax;

        var adjustment = rect.yMax > bounds.yMax ? bounds.yMax - rect.yMax : 0f;
        if (rect.yMin + adjustment < bounds.yMin)
            adjustment += bounds.yMin - (rect.yMin + adjustment);
        return adjustment;
    }

    internal static bool Collides(PairSide side, Rect primaryBounds, Rect panelBounds, float gap) =>
        side == PairSide.Right
            ? panelBounds.xMin < primaryBounds.xMax + gap - NativePairedTooltipMetrics.Epsilon
            : panelBounds.xMax > primaryBounds.xMin - gap + NativePairedTooltipMetrics.Epsilon;

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

    internal static Rect Union(Rect first, Rect second) =>
        Rect.MinMaxRect(
            Mathf.Min(first.xMin, second.xMin),
            Mathf.Min(first.yMin, second.yMin),
            Mathf.Max(first.xMax, second.xMax),
            Mathf.Max(first.yMax, second.yMax)
        );

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

    // Cap the scale so enlarging a panel never lowers its trim budget below the unscaled case.
    internal static float ContentHeightLimit(
        float canvasHeight,
        float canvasMargin,
        float canvasUnitsPerLocalUnit
    )
    {
        var insetHeight = Mathf.Max(
            0f,
            canvasHeight - 2f * Mathf.Min(canvasMargin, canvasHeight * 0.5f)
        );
        return insetHeight / Mathf.Clamp(canvasUnitsPerLocalUnit, 0.0001f, 1f);
    }

    internal static int MaximumVisibleRows(float contentHeightLimit, float minimumRowHeight)
    {
        if (
            float.IsNaN(contentHeightLimit)
            || float.IsInfinity(contentHeightLimit)
            || float.IsNaN(minimumRowHeight)
            || float.IsInfinity(minimumRowHeight)
            || minimumRowHeight <= 0f
        )
            return int.MaxValue;
        var rows = Mathf.Ceil(
            (Mathf.Max(0f, contentHeightLimit) + NativePairedTooltipMetrics.Epsilon)
                / minimumRowHeight
        );
        return rows >= int.MaxValue ? int.MaxValue : (int)rows;
    }
}
