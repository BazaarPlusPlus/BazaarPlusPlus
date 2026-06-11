#nullable enable
using System;

namespace BazaarPlusPlus.GameInterop.ItemBoardPreview;

internal readonly struct ItemBoardSlotGridRect
{
    public ItemBoardSlotGridRect(float x, float y, float width, float height)
    {
        X = x;
        Y = y;
        Width = width;
        Height = height;
    }

    public float X { get; }
    public float Y { get; }
    public float Width { get; }
    public float Height { get; }
    public float CenterX => X + Width * 0.5f;
    public float CenterY => Y + Height * 0.5f;
}

internal static class ItemBoardSlotGridGeometry
{
    public const int SocketCount = 10;

    public static ItemBoardSlotGridRect ResolveOccupiedRect(
        float rowWidth,
        float rowHeight,
        int socketIndex,
        int span,
        float horizontalInset,
        float verticalInset
    )
    {
        var safeWidth = Math.Max(1f, rowWidth);
        var safeHeight = Math.Max(1f, rowHeight);
        var safeSocket = Math.Clamp(socketIndex, 0, SocketCount - 1);
        var safeSpan = Math.Clamp(span, 1, SocketCount);
        var clampedSpan = Math.Min(safeSpan, SocketCount - safeSocket);
        var slotWidth = safeWidth / SocketCount;
        var left = safeSocket * slotWidth;
        var width = clampedSpan * slotWidth;

        var insetX = Math.Max(0f, horizontalInset);
        var insetY = Math.Max(0f, verticalInset);
        return new ItemBoardSlotGridRect(
            Math.Min(left + insetX, safeWidth),
            Math.Min(insetY, safeHeight),
            Math.Max(1f, width - insetX * 2f),
            Math.Max(1f, safeHeight - insetY * 2f)
        );
    }

    public static float ResolveHeightScale(float frameHeight, float targetHeight, float maxScale)
    {
        var safeFrameHeight = Math.Max(1f, frameHeight);
        var safeTargetHeight = Math.Max(1f, targetHeight);

        var scale = safeTargetHeight / safeFrameHeight;
        var safeMaxScale = Math.Max(0.05f, maxScale);
        scale = Math.Clamp(scale, 0.05f, safeMaxScale);
        if (float.IsNaN(scale) || float.IsInfinity(scale))
            return 1f;

        return scale;
    }

    public static float ResolveScaledTargetHeight(
        float slotHeight,
        float boardNativeHeight,
        float boardScale,
        float maxHeightRatio
    )
    {
        var safeSlotHeight = Math.Max(1f, slotHeight);
        var scaledBoardHeight = Math.Max(1f, boardNativeHeight * Math.Max(0.05f, boardScale));
        var ratio = Math.Clamp(maxHeightRatio, 0f, 1f);
        return Math.Max(1f, Math.Min(safeSlotHeight, scaledBoardHeight * ratio));
    }
}
