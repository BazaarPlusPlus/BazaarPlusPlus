#nullable enable
using UnityEngine;

namespace BazaarPlusPlus.GameInterop.ItemBoardPreview;

internal static class ItemBoardSocketLayout
{
    public const int SocketCount = ItemBoardSlotGridGeometry.SocketCount;
    public const int NativeBoardWidth = 2600;
    public const int NativeBoardHeight = 600;
    public const float NativeSocketHeightPixels = 484f;
    public const float NativeSocketPitchPixels = 240f;
    public const float FrameHeightOverSocket = 1.03704f;

    // Medium-card frame anatomy (bundle dump): the tier frame renders 1.05787x wider than
    // the 1.04-aspect card body. Per slot of span that is the widest frame of the three
    // sizes (small 1.04452x on 0.52; large's 1.41548x is decorative flourish), so medium
    // binds any "frame borders must not cross" fit.
    public const float NativeMediumFrameWidthOverRoot = 1.05787f;
    public const float NativeMediumBodyAspect = 1.04f;

    private const float HorizontalPaddingFraction = 0f;

    // Native Tooltip_MonsterBoard_P sockets are width-0 point pins on a 240px x-pitch;
    // the native card root stretches to the socket height, which controls gem proportions.
    private const float FallbackSocketWidthPixels = NativeSocketPitchPixels;
    private const float FallbackSocketHeightPixels = NativeSocketHeightPixels;

    public static RectTransform[] BuildSockets(RectTransform parent, int layer, string objectPrefix)
    {
        var sockets = new RectTransform[SocketCount];
        var step = (1f - HorizontalPaddingFraction * 2f) / SocketCount;
        var firstCenter = HorizontalPaddingFraction + step * 0.5f;

        for (var i = 0; i < SocketCount; i++)
        {
            var go = new GameObject($"{objectPrefix}_{i}", typeof(RectTransform));
            go.layer = layer;
            var socket = go.GetComponent<RectTransform>();
            socket.SetParent(parent, worldPositionStays: false);

            var anchorX = firstCenter + step * i;
            socket.anchorMin = new Vector2(anchorX, 0.5f);
            socket.anchorMax = new Vector2(anchorX, 0.5f);
            socket.pivot = new Vector2(0.5f, 0.5f);
            socket.sizeDelta = new Vector2(FallbackSocketWidthPixels, FallbackSocketHeightPixels);
            socket.anchoredPosition = Vector2.zero;
            sockets[i] = socket;
        }

        return sockets;
    }
}
