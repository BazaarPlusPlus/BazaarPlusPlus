#nullable enable
using System;

namespace BazaarPlusPlus.Game.CombatStatusBar;

internal sealed partial class CombatStatusBar
{
    internal static float ResolveRoundedRectAlpha(
        float pixelCenterX,
        float pixelCenterY,
        int size,
        float radius,
        float edgeSoftness
    )
    {
        if (size <= 0)
            return 0f;

        var halfSize = size * 0.5f;
        var clampedRadius = Clamp(radius, 0f, halfSize);
        var softness = Math.Max(edgeSoftness, 0.0001f);
        var innerHalfExtent = halfSize - clampedRadius;

        var distanceX = Math.Abs(pixelCenterX - halfSize) - innerHalfExtent;
        var distanceY = Math.Abs(pixelCenterY - halfSize) - innerHalfExtent;
        var outsideX = Math.Max(distanceX, 0f);
        var outsideY = Math.Max(distanceY, 0f);
        var outsideDistance = (float)Math.Sqrt(outsideX * outsideX + outsideY * outsideY);
        var insideDistance = Math.Min(Math.Max(distanceX, distanceY), 0f);
        var signedDistance = outsideDistance + insideDistance - clampedRadius;

        return Clamp(0.5f - signedDistance / softness, 0f, 1f);
    }

    private static float Clamp(float value, float min, float max)
    {
        if (value < min)
            return min;

        return value > max ? max : value;
    }
}
