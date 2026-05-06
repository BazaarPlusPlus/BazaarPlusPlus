#nullable enable
using TMPro;
using UnityEngine;

namespace BazaarPlusPlus.Game.Lobby;

internal static class LobbyPanelLayout
{
    public const float PanelHorizontalPadding = 10f;
    public const float PanelTopPadding = 8f;
    public const float PanelBottomPadding = 10f;
    public const float HeaderHeight = 18f;
    public const float HeaderToEntriesSpacing = 8f;
    public const float EmptyStateEntriesHeight = 24f;

    public static TextMeshProUGUI CreateText(
        string objectName,
        Transform parent,
        float fontSize,
        TextAlignmentOptions alignment,
        Color color,
        bool wrap = false
    )
    {
        var textObject = new GameObject(
            objectName,
            typeof(RectTransform),
            typeof(TextMeshProUGUI)
        );
        var textRect = textObject.GetComponent<RectTransform>();
        textRect.SetParent(parent, worldPositionStays: false);

        var text = textObject.GetComponent<TextMeshProUGUI>();
        text.fontSize = fontSize;
        text.alignment = alignment;
        text.color = color;
        text.raycastTarget = false;
        text.textWrappingMode = wrap ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
        return text;
    }

    public static float CalculatePanelHeight(
        int rows,
        float entryHeight,
        float entryVerticalSpacing,
        float emptyEntriesHeight = EmptyStateEntriesHeight
    )
    {
        var entriesHeight =
            rows <= 0
                ? emptyEntriesHeight
                : (rows * entryHeight) + ((rows - 1) * entryVerticalSpacing);
        return PanelTopPadding
            + HeaderHeight
            + HeaderToEntriesSpacing
            + entriesHeight
            + PanelBottomPadding;
    }

    public static void ClearChildren(RectTransform root)
    {
        for (var index = root.childCount - 1; index >= 0; index--)
        {
            var child = root.GetChild(index);
            if (child != null)
                Object.Destroy(child.gameObject);
        }
    }

    public static Vector2 GridAnchoredPosition(
        int index,
        int columnCount,
        float entryWidth,
        float entryHeight,
        float horizontalSpacing,
        float verticalSpacing
    )
    {
        return new Vector2(
            (index % columnCount) * (entryWidth + horizontalSpacing),
            -(index / columnCount) * (entryHeight + verticalSpacing)
        );
    }
}
