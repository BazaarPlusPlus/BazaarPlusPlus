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

    public static RectTransform BuildSocket(
        RectTransform parent,
        int layer,
        string objectName,
        int templateIndex = 0
    )
    {
        var go = new GameObject(objectName, typeof(RectTransform));
        go.layer = layer;
        var socket = go.GetComponent<RectTransform>();
        socket.SetParent(parent, worldPositionStays: false);
        ApplySocketTemplate(socket, templateIndex);
        return socket;
    }

    public static RectTransform[] BuildSockets(RectTransform parent, int layer, string objectPrefix)
    {
        var sockets = new RectTransform[SocketCount];
        var step = (1f - HorizontalPaddingFraction * 2f) / SocketCount;
        var firstCenter = HorizontalPaddingFraction + step * 0.5f;

        for (var i = 0; i < SocketCount; i++)
        {
            var socket = BuildSocket(parent, layer, $"{objectPrefix}_{i}", i);
            var anchorX = firstCenter + step * i;
            socket.anchorMin = new Vector2(anchorX, 0.5f);
            socket.anchorMax = new Vector2(anchorX, 0.5f);
            socket.anchoredPosition = Vector2.zero;
            sockets[i] = socket;
        }

        return sockets;
    }

    private static void ApplySocketTemplate(RectTransform socket, int templateIndex)
    {
        var templates = NativeCardPreviewPrefabResolver.TryGetSocketTemplates();
        if (templates != null && templateIndex >= 0 && templateIndex < templates.Length)
        {
            socket.sizeDelta = templates[templateIndex].SizeDelta;
            socket.pivot = templates[templateIndex].Pivot;
            return;
        }

        socket.sizeDelta = new Vector2(FallbackSocketWidthPixels, FallbackSocketHeightPixels);
        socket.pivot = new Vector2(0.5f, 0.5f);
    }
}
