#nullable enable
using System;

namespace BazaarPlusPlus.GameInterop.ItemBoardPreview;

internal static class ItemBoardSocketResolver
{
    public static int ResolveIndex(
        int socketCount,
        int? requestedIndex,
        int fallbackIndex,
        int span
    )
    {
        if (socketCount <= 0)
            return -1;

        var lastValidStart = socketCount - span;
        if (lastValidStart < 0)
            return -1;

        var index = requestedIndex ?? fallbackIndex;
        return Math.Clamp(index, 0, lastValidStart);
    }
}
