#nullable enable
using BazaarPlusPlus.GameInterop.CardPreview;
using UnityEngine;

namespace BazaarPlusPlus.GameInterop.ItemBoardPreview;

internal static class ItemBoardSocketLayout
{
    public const int SocketCount = ItemBoardSlotGridGeometry.SocketCount;
    public const int NativeBoardWidth = 2600;
    public const int NativeBoardHeight = 600;

    private const float HorizontalPaddingFraction = 0f;
    private const float FallbackSocketWidthPixels = 240f;
    private const float FallbackSocketHeightPixels = 320f;

    public static RectTransform[] BuildSockets(RectTransform parent, int layer, string objectPrefix)
    {
        var sockets = new RectTransform[SocketCount];
        var templates = NativeCardPreviewPrefabResolver.TryGetSocketTemplates();
        var step = (1f - HorizontalPaddingFraction * 2f) / SocketCount;
        var firstCenter = HorizontalPaddingFraction + step * 0.5f;

        for (var i = 0; i < SocketCount; i++)
        {
            var go = new GameObject($"{objectPrefix}_{i}", typeof(RectTransform));
            go.layer = layer;
            var socket = go.GetComponent<RectTransform>();
            socket.SetParent(parent, worldPositionStays: false);

            Vector2 sizeDelta;
            Vector2 pivot;
            if (templates != null && i < templates.Length)
            {
                sizeDelta = templates[i].SizeDelta;
                pivot = templates[i].Pivot;
            }
            else
            {
                sizeDelta = new Vector2(FallbackSocketWidthPixels, FallbackSocketHeightPixels);
                pivot = new Vector2(0.5f, 0.5f);
            }

            var anchorX = firstCenter + step * i;
            socket.anchorMin = new Vector2(anchorX, 0.5f);
            socket.anchorMax = new Vector2(anchorX, 0.5f);
            socket.pivot = pivot;
            socket.sizeDelta = sizeDelta;
            socket.anchoredPosition = Vector2.zero;
            sockets[i] = socket;
        }

        return sockets;
    }
}
