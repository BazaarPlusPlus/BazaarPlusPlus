#nullable enable
using BazaarPlusPlus.Infrastructure.UiTokens;

namespace BazaarPlusPlus.Game.CollectionPanel.Grid;

internal static class CollectionGridHostConstants
{
    // A full-screen GraphicRaycaster between the layers would intercept clicks and wheel events before UITK.
    public const int OverlaySortingOrder = BppOverlaySorting.NativeCardPreview;

    // Raycaster mode lets the card prefab's RawImage swallow wheel events. Use it for diagnostics only.
    // Keep this static readonly so either setting compiles without unreachable-code warnings.
    public static readonly bool UsePolledHover = true;

    // On this UITK version, intercepting WheelEvent to lerp scrollOffset conflicts with ScrollView's built-in handler.
    public const float MouseWheelScrollPoints = 120f;
}
