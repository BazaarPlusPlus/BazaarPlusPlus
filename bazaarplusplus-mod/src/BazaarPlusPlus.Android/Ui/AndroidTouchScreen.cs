using BazaarPlusPlus.Core.Ui;
using UnityEngine;

namespace BazaarPlusPlus.Android.Ui;

/// <summary>
/// The screen as Core's <see cref="TouchMetrics"/> sees it: Unity's pixel size, density, and safe
/// area against the touch canvas's reference height (<see cref="AndroidUiKit.ReferenceResolution"/>,
/// matched by height). A panel built on it compares <see cref="Key"/> each frame and lays itself out
/// again when the resolution, density, or safe area changes. Main thread only.
/// </summary>
internal static class AndroidTouchScreen
{
    /// <summary>What the layout depends on; a change means a new layout.</summary>
    public static string Key()
    {
        var safe = Screen.safeArea;
        return $"{Screen.width}x{Screen.height}@{Screen.dpi:0.#}|{safe.x:0},{safe.y:0},{safe.width:0},{safe.height:0}";
    }

    public static TouchMetrics Read()
    {
        var safe = Screen.safeArea;
        return TouchMetrics.From(
            Math.Max(1, Screen.width),
            Math.Max(1, Screen.height),
            Screen.dpi,
            AndroidUiKit.ReferenceResolution.y,
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
            AndroidUiKit.Rect(name, parent, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero),
            area
        );
}
