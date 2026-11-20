#nullable enable
namespace BazaarPlusPlus.Game.ItemEnchantPreview;

public static class ItemEnchantPreviewTooltipLayerPolicy
{
    // Pooled tooltip canvases tie at sort order 150, where the locked clone wins. One step above breaks the tie without overtaking unrelated overlays.
    public const int ElevatedSortingStep = 1;

    public static int ElevatedSortingOrder(int originalSortingOrder) =>
        originalSortingOrder + ElevatedSortingStep;
}
