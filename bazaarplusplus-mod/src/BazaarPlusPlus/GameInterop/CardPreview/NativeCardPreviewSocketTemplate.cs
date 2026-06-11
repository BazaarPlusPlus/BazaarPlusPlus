#nullable enable
using UnityEngine;

namespace BazaarPlusPlus.GameInterop.CardPreview;

internal readonly struct NativeCardPreviewSocketTemplate
{
    public NativeCardPreviewSocketTemplate(
        Vector2 anchoredPosition,
        Vector2 sizeDelta,
        Vector2 anchorMin,
        Vector2 anchorMax,
        Vector2 pivot
    )
    {
        AnchoredPosition = anchoredPosition;
        SizeDelta = sizeDelta;
        AnchorMin = anchorMin;
        AnchorMax = anchorMax;
        Pivot = pivot;
    }

    public Vector2 AnchoredPosition { get; }
    public Vector2 SizeDelta { get; }
    public Vector2 AnchorMin { get; }
    public Vector2 AnchorMax { get; }
    public Vector2 Pivot { get; }
}
