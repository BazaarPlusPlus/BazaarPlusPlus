#nullable enable
namespace BazaarPlusPlus.Game.ItemEnchantPreview;

public static class ItemEnchantPreviewTooltipLayerPolicy
{
    // Probe-measured (docs/drafts/2026-07-08-tooltip-overlay-third-recurrence.md): every
    // pooled tooltip clone's root canvas shares one sorting order (150), so overlapping
    // clones tie and the locked clone wins, covering the hovered tooltip that carries the
    // BPP preview. One step above the clone's own original order wins that tie without
    // climbing over unrelated overlays.
    public const int ElevatedSortingStep = 1;

    public static int ElevatedSortingOrder(int originalSortingOrder) =>
        originalSortingOrder + ElevatedSortingStep;
}
