#nullable enable
namespace BazaarPlusPlus.GameInterop.Tooltips;

internal static class NativeInspectionSizing
{
    // Preserve the native description scale unless it overflows the available space.
    public static float FitScale(
        float width,
        float height,
        float availableWidth,
        float availableHeight
    ) =>
        Math.Min(
            1f,
            Math.Min(
                Math.Max(1f, availableWidth) / Math.Max(1f, width),
                Math.Max(1f, availableHeight) / Math.Max(1f, height)
            )
        );
}
