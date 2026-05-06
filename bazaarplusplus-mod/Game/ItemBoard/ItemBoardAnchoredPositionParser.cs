#nullable enable
using System.Globalization;
using UnityEngine;

namespace BazaarPlusPlus.Game.ItemBoard;

internal static class ItemBoardAnchoredPositionParser
{
    public static bool IsAuto(string? rawValue)
    {
        return !string.IsNullOrWhiteSpace(rawValue)
            && rawValue.Trim().Equals("auto", System.StringComparison.OrdinalIgnoreCase);
    }

    public static bool TryParse(string? rawValue, out Vector2 anchoredPosition)
    {
        anchoredPosition = default;
        if (string.IsNullOrWhiteSpace(rawValue))
            return false;

        var trimmed = rawValue.Trim();
        if (IsAuto(trimmed))
            return false;

        var parts = trimmed.Split(',');
        if (parts.Length != 2)
            return false;

        if (
            !float.TryParse(
                parts[0].Trim(),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var x
            )
            || !float.TryParse(
                parts[1].Trim(),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var y
            )
        )
        {
            return false;
        }

        anchoredPosition = new Vector2(x, y);
        return true;
    }
}
