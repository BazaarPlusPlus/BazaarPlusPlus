#nullable enable
namespace BazaarPlusPlus.Game.CombatStatusBar;

internal static class CombatStatusBarLayout
{
    // Screen coordinates have their origin at the bottom-left.
    internal static bool TryPlace(
        float left,
        float right,
        float portraitBottom,
        float viewportLeft,
        float viewportRight,
        float viewportBottom,
        float viewportTop,
        float uiScale,
        out float height
    )
    {
        height = portraitBottom - 3f * uiScale - viewportBottom;
        return float.IsFinite(left)
            && float.IsFinite(right)
            && float.IsFinite(height)
            && float.IsFinite(uiScale)
            && uiScale > 0f
            && right - left >= 120f * uiScale
            && left >= viewportLeft
            && right <= viewportRight
            && portraitBottom <= viewportTop
            && height >= 24f * uiScale;
    }
}
