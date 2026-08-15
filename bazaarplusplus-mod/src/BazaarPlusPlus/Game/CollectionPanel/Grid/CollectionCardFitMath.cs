#nullable enable
namespace BazaarPlusPlus.Game.CollectionPanel.Grid;

// Pure card-fit math for the Collection grid. UnityEngine-free so scale/position rules are
// unit-tested in tests/CollectionGridLayout.Tests without booting the game client. Measurement
// and RectTransform writes live in NativeCardCellFitter; this type only computes numbers.
internal readonly struct CardVisualBounds
{
    public CardVisualBounds(float width, float height, float centerX, float centerY)
    {
        Width = width;
        Height = height;
        CenterX = centerX;
        CenterY = centerY;
    }

    public float Width { get; }
    public float Height { get; }
    public float CenterX { get; }
    public float CenterY { get; }
}

internal static class CollectionCardFitMath
{
    // Scale to the cell HEIGHT so every card in a shelf renders the same height. Clamp item
    // cards by body width, not frame width: Large frame art has native side flourishes that
    // overhang the 3:2 body and should not make only Large cards shorter. When aspectRatio is
    // usable, body width is derived from body height * aspect; otherwise fall back to measured
    // visual width (which may include frame flourishes).
    public static float ComputeScale(
        CardVisualBounds bounds,
        float? aspectRatio,
        CollectionGridRect cellRect,
        float gap,
        float inset,
        float frameHeightOverSocket
    )
    {
        var natW = bounds.Width;
        var natH = bounds.Height;

        var targetH = Math.Max(1f, cellRect.Height * (1f - 2f * inset));
        var scale = targetH / natH;
        var maxWidth = cellRect.Width + gap;
        var bodyH = natH / frameHeightOverSocket;
        var bodyW = natW;
        if (aspectRatio is float ar && ar > 0.01f && !float.IsNaN(ar) && !float.IsInfinity(ar))
        {
            bodyW = ar * bodyH;
        }
        if (bodyW * scale > maxWidth)
            scale = maxWidth / bodyW;
        if (scale <= 0f || float.IsNaN(scale) || float.IsInfinity(scale))
            scale = 1f;
        return scale;
    }

    // Board pivot is top-left (y increases downward in content space, anchored Y goes negative).
    // Place the measured native visual center on the cell center; item-card root RectTransforms
    // can report a zero rect, so the measured bounds — not root.rect — drive placement.
    // scaleX/scaleY match RectTransform.localScale so a non-uniform write stays call-equivalent.
    public static (float X, float Y) ComputeAnchoredPosition(
        CardVisualBounds bounds,
        float scaleX,
        float scaleY,
        CollectionGridRect cellRect,
        float scrollY
    )
    {
        var screenTop = cellRect.Y - scrollY;
        var targetCenterX = cellRect.X + cellRect.Width * 0.5f;
        var targetCenterY = -(screenTop + cellRect.Height * 0.5f);
        return (targetCenterX - bounds.CenterX * scaleX, targetCenterY - bounds.CenterY * scaleY);
    }
}
