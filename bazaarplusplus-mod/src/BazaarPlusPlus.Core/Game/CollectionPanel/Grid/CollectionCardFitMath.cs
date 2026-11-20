#nullable enable
namespace BazaarPlusPlus.Game.CollectionPanel.Grid;

public readonly struct CardVisualBounds
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

public static class CollectionCardFitMath
{
    // Clamp to the card body's width so Large cards keep the shelf height even when their native frames overhang.
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

    // The grid is top-left and downward-positive. Use measured visual bounds because item-card root RectTransforms can report a zero rect.
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
