using BazaarPlusPlus.Core.Ui;
using UnityEngine;

namespace BazaarPlusPlus.Mobile.Ui;

/// <summary>
/// The screen as Core's <see cref="TouchMetrics"/> sees it: Unity's pixel size, density, and safe
/// area against the touch canvas's reference height (<see cref="UiKit.ReferenceResolution"/>,
/// matched by height). A panel built on it compares <see cref="Key"/> each frame and lays itself out
/// again when the resolution, density, or safe area changes. Main thread only.
/// </summary>
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

    /// <summary>
    /// What the layout depends on; a change means a new layout. Panels call it every frame, so
    /// the string is formatted again only when one of its inputs changed.
    /// </summary>
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

    /// <summary>
    /// Places <paramref name="rect"/> at <paramref name="area"/>, given top-down from the top-left
    /// corner of a parent that spans the canvas (or of a list's content).
    /// </summary>
    public static RectTransform Place(RectTransform rect, TouchRect area)
    {
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(area.Left, -area.Top);
        rect.sizeDelta = new Vector2(area.Width, area.Height);
        return rect;
    }

    /// <summary>A new child placed at <paramref name="area"/>.</summary>
    public static RectTransform Child(string name, Transform parent, TouchRect area) =>
        Place(
            UiKit.Rect(name, parent, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero),
            area
        );
}
