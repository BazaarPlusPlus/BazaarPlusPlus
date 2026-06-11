#nullable enable

using BazaarPlusPlus.Infrastructure.UiTokens;

namespace BazaarPlusPlus.GameInterop.ItemBoardPreview;

internal sealed class ItemBoardPreviewOptions
{
    public int Layer { get; init; } = 30;

    public int SortingOrder { get; init; } = BppOverlaySorting.NativeCardPreview;

    public ItemBoardPreviewLayoutMode LayoutMode { get; init; } =
        ItemBoardPreviewLayoutMode.Socketed;

    public bool ShowHover { get; init; } = true;

    public bool UseCanvasGroup { get; init; }

    public string LogComponent { get; init; } = "ItemBoardPreviewSurface";

    public float SlotGridHorizontalInsetPixels { get; init; } = 8f;

    public float SlotGridVerticalInsetPixels { get; init; } = 6f;

    public float SlotGridMaxHeightRatio { get; init; } = 0.96f;

    public float SlotGridMaxScale { get; init; } = 10f;
}
