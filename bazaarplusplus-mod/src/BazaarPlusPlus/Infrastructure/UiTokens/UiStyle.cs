#nullable enable
using UnityEngine;
using UnityEngine.UIElements;

namespace BazaarPlusPlus.Infrastructure.UiTokens;

internal static class UiStyle
{
    public static void FixedWidth(IStyle style, float width)
    {
        style.width = width;
        style.minWidth = width;
        style.maxWidth = width;
    }

    public static void FixedHeight(IStyle style, float height)
    {
        style.height = height;
        style.minHeight = height;
        style.maxHeight = height;
    }

    public static void FixedSize(IStyle style, float width, float height)
    {
        FixedWidth(style, width);
        FixedHeight(style, height);
    }

    public static void Padding(IStyle style, float value)
    {
        style.paddingLeft = value;
        style.paddingRight = value;
        style.paddingTop = value;
        style.paddingBottom = value;
    }

    public static void Padding(IStyle style, float horizontal, float vertical)
    {
        style.paddingLeft = horizontal;
        style.paddingRight = horizontal;
        style.paddingTop = vertical;
        style.paddingBottom = vertical;
    }

    public static void HorizontalPadding(IStyle style, float value)
    {
        style.paddingLeft = value;
        style.paddingRight = value;
    }

    public static void Radius(IStyle style, float value)
    {
        style.borderTopLeftRadius = value;
        style.borderTopRightRadius = value;
        style.borderBottomLeftRadius = value;
        style.borderBottomRightRadius = value;
    }

    public static void Border(IStyle style, float width, Color color)
    {
        BorderWidth(style, width);
        BorderColor(style, color);
    }

    public static void BorderWidth(IStyle style, float width)
    {
        style.borderLeftWidth = width;
        style.borderRightWidth = width;
        style.borderTopWidth = width;
        style.borderBottomWidth = width;
    }

    public static void BorderColor(IStyle style, Color color)
    {
        style.borderLeftColor = color;
        style.borderRightColor = color;
        style.borderTopColor = color;
        style.borderBottomColor = color;
    }
}
