#nullable enable

namespace BazaarPlusPlus.GameInterop.CardPreview;

internal enum NativeCardPreviewHorizontalAlignment
{
    Center,
    Left,
}

internal readonly record struct NativeCardPreviewSlotFit(
    float Scale,
    float PositionX,
    float PositionY,
    float VisibleWidth
);

internal readonly record struct NativeCardPreviewVisibleHorizontalFit(
    float PositionCorrection,
    float VisibleWidth
);

internal static class NativeCardPreviewSlotFitMath
{
    private const float MinimumDimension = 0.01f;

    internal static bool TryResolve(
        float visualMinX,
        float visualMinY,
        float visualMaxX,
        float visualMaxY,
        float slotWidth,
        float slotHeight,
        float slotCenterX,
        float slotCenterY,
        NativeCardPreviewHorizontalAlignment horizontalAlignment,
        out NativeCardPreviewSlotFit fit
    )
    {
        var visualWidth = visualMaxX - visualMinX;
        var visualHeight = visualMaxY - visualMinY;
        if (
            visualWidth <= MinimumDimension
            || visualHeight <= MinimumDimension
            || slotWidth <= MinimumDimension
            || slotHeight <= MinimumDimension
        )
        {
            fit = default;
            return false;
        }

        var scale = MathF.Min(slotWidth / visualWidth, slotHeight / visualHeight);
        var visualCenterX = (visualMinX + visualMaxX) * 0.5f;
        var visualCenterY = (visualMinY + visualMaxY) * 0.5f;
        fit = new NativeCardPreviewSlotFit(
            scale,
            horizontalAlignment == NativeCardPreviewHorizontalAlignment.Left
                ? slotCenterX - slotWidth * 0.5f - visualMinX * scale
                : slotCenterX - visualCenterX * scale,
            slotCenterY - visualCenterY * scale,
            visualWidth * scale
        );
        return true;
    }

    internal static bool TryResolveVisibleHorizontalFit(
        float visibleMinX,
        float visibleMaxX,
        float slotMinX,
        out NativeCardPreviewVisibleHorizontalFit fit
    )
    {
        var visibleWidth = visibleMaxX - visibleMinX;
        if (
            visibleWidth <= MinimumDimension
            || float.IsNaN(visibleWidth)
            || float.IsInfinity(visibleWidth)
        )
        {
            fit = default;
            return false;
        }

        fit = new NativeCardPreviewVisibleHorizontalFit(slotMinX - visibleMinX, visibleWidth);
        return true;
    }
}
