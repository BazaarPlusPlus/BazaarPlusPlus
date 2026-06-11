#nullable enable
using System;

namespace BazaarPlusPlus.Game.Settings;

internal readonly struct BppSettingsDockLocalPosition(float x, float y, float z)
{
    internal float X { get; } = x;

    internal float Y { get; } = y;

    internal float Z { get; } = z;
}

internal static class BppSettingsDockGeometry
{
    internal static BppSettingsDockLocalPosition CalculateDockButtonLocalPosition(
        float anchorCenterLocalX,
        float anchorCenterLocalY,
        float anchorLeftLocalX,
        float anchorRightLocalX,
        float anchorTopLocalY,
        float anchorBottomLocalY,
        float currentLocalZ,
        BppSettingsDockPlacement placement
    )
    {
        var widthLocal = Math.Abs(anchorRightLocalX - anchorLeftLocalX);
        var heightLocal = Math.Abs(anchorTopLocalY - anchorBottomLocalY);
        var worldLeftDirectionLocal = (float)Math.Sign(anchorLeftLocalX - anchorRightLocalX);
        if (worldLeftDirectionLocal == 0f)
            worldLeftDirectionLocal = -1f;

        var worldUpDirectionLocal = (float)Math.Sign(anchorTopLocalY - anchorBottomLocalY);
        if (worldUpDirectionLocal == 0f)
            worldUpDirectionLocal = 1f;

        if (placement.Side == BppSettingsDockSide.AboveAnchor)
        {
            return new BppSettingsDockLocalPosition(
                anchorCenterLocalX,
                anchorCenterLocalY + worldUpDirectionLocal * (heightLocal + placement.SiblingGap),
                currentLocalZ
            );
        }

        var direction =
            placement.Side == BppSettingsDockSide.LeftOfAnchor
                ? worldLeftDirectionLocal
                : -worldLeftDirectionLocal;
        return new BppSettingsDockLocalPosition(
            anchorCenterLocalX + direction * (widthLocal + placement.SiblingGap),
            anchorCenterLocalY,
            currentLocalZ
        );
    }

    internal static float CalculatePanelLocalScale(
        float targetOnScreenScale,
        float cloneLocalScale
    ) => cloneLocalScale > 0.0001f ? targetOnScreenScale / cloneLocalScale : targetOnScreenScale;
}
