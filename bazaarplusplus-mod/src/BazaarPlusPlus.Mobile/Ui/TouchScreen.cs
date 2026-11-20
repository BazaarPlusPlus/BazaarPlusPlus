using BazaarPlusPlus.Core.Ui;
using UnityEngine;

namespace BazaarPlusPlus.Mobile.Ui;

internal static class TouchScreen
{
    private static int _keyWidth;
    private static int _keyHeight;
    private static float _keyDpi;
    private static float _keySafeX;
    private static float _keySafeY;
    private static float _keySafeWidth;
    private static float _keySafeHeight;
    private static string? _key;

    // Checked every frame, so format the layout key only when its inputs change.
    public static string Key()
    {
        var width = Screen.width;
        var height = Screen.height;
        var dpi = Screen.dpi;
        var safe = Screen.safeArea;
        if (
            _key != null
            && width == _keyWidth
            && height == _keyHeight
            && dpi == _keyDpi
            && safe.x == _keySafeX
            && safe.y == _keySafeY
            && safe.width == _keySafeWidth
            && safe.height == _keySafeHeight
        )
            return _key;
        _keyWidth = width;
        _keyHeight = height;
        _keyDpi = dpi;
        _keySafeX = safe.x;
        _keySafeY = safe.y;
        _keySafeWidth = safe.width;
        _keySafeHeight = safe.height;
        return _key =
            $"{width}x{height}@{dpi:0.#}|{safe.x:0},{safe.y:0},{safe.width:0},{safe.height:0}";
    }

    public static TouchMetrics Read()
    {
        var safe = Screen.safeArea;
        return TouchMetrics.From(
            Math.Max(1, Screen.width),
            Math.Max(1, Screen.height),
            Screen.dpi,
            UiKit.ReferenceResolution.y,
            safe.x,
            safe.y,
            safe.width,
            safe.height
        );
    }

    // The rect uses top-down coordinates from the parent's top-left corner.
    public static RectTransform Place(RectTransform rect, TouchRect area)
    {
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(area.Left, -area.Top);
        rect.sizeDelta = new Vector2(area.Width, area.Height);
        return rect;
    }

    public static RectTransform Child(string name, Transform parent, TouchRect area) =>
        Place(
            UiKit.Rect(name, parent, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero),
            area
        );
}
