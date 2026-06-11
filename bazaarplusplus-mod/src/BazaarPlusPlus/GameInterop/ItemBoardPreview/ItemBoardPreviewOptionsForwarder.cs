#nullable enable

namespace BazaarPlusPlus.GameInterop.ItemBoardPreview;

internal static class ItemBoardPreviewOptionsForwarder
{
    public static ItemBoardPreviewOptions ForSurface(ItemBoardPreviewOptions? options)
    {
        var source = options ?? new ItemBoardPreviewOptions();
        return new ItemBoardPreviewOptions
        {
            Layer = source.Layer,
            SortingOrder = source.SortingOrder,
            LayoutMode = source.LayoutMode,
            ShowHover = source.ShowHover,
            UseCanvasGroup = source.UseCanvasGroup,
            LogComponent = source.LogComponent,
            SlotGridHorizontalInsetPixels = source.SlotGridHorizontalInsetPixels,
            SlotGridVerticalInsetPixels = source.SlotGridVerticalInsetPixels,
            SlotGridMaxHeightRatio = source.SlotGridMaxHeightRatio,
            SlotGridMaxScale = source.SlotGridMaxScale,
        };
    }
}
