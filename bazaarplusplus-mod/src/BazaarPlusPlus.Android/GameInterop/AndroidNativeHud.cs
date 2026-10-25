using BazaarPlusPlus.Core.Ui;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using UnityEngine.UI;

namespace BazaarPlusPlus.Android.GameInterop;

/// <summary>Current native HUD anchors, projected into the height-scaled touch canvas.</summary>
internal static class AndroidNativeHud
{
    private static Il2CppTheBazaar.PopupManager? _popup;

    // One buffer for every read: main thread only, and GetWorldCorners overwrites all four.
    private static Il2CppStructArray<Vector3>? _corners;

    /// <summary>The game's popup manager, once its scene has made one.</summary>
    public static Il2CppTheBazaar.PopupManager? Popups
    {
        get
        {
            if (_popup == null)
                _popup = UnityEngine.Object.FindObjectOfType<Il2CppTheBazaar.PopupManager>();
            return _popup;
        }
    }

    public static bool HasPopup =>
        Popups is { } popup && (popup.IsAnyPopupOpen || popup.IsSettingsOpen);

    /// <summary>Whether the native card drawer is showing an inspected card.</summary>
    public static bool IsInspectingCard =>
        Il2CppTheBazaar.Data.TooltipParentComponent?.CardTooltipController?.CurrentCard != null;

    public static Button? FindSettingsButton()
    {
        foreach (var fight in Resources.FindObjectsOfTypeAll<Il2Cpp.FightMenuDialog>())
        {
            var button = fight.SettingButton?.GetButton();
            if (button != null && button.gameObject.activeInHierarchy)
                return button;
        }
        foreach (var view in Resources.FindObjectsOfTypeAll<Il2Cpp.SettingDialogsView>())
        {
            foreach (
                var button in new[]
                {
                    view.MainMenuSettingOptionButton,
                    view.HeroSelectSettingOptionButton,
                }
            )
                if (button != null && button.gameObject.activeInHierarchy)
                    return button;
        }
        return null;
    }

    public static TouchRect ScreenBounds(RectTransform rect, float referenceHeight) =>
        ScreenBounds(rect, rect.GetComponentInParent<Canvas>().rootCanvas, referenceHeight);

    /// <summary>
    /// <paramref name="rect"/>'s screen rect, for a caller that reads it every frame and keeps
    /// the rect's <paramref name="rootCanvas"/> from the first read.
    /// </summary>
    public static TouchRect ScreenBounds(
        RectTransform rect,
        Canvas rootCanvas,
        float referenceHeight
    )
    {
        var camera =
            rootCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : rootCanvas.worldCamera;
        var corners = _corners ??= new Il2CppStructArray<Vector3>(4L);
        rect.GetWorldCorners(corners);
        var bottom = RectTransformUtility.WorldToScreenPoint(camera, corners[0]);
        var top = RectTransformUtility.WorldToScreenPoint(camera, corners[2]);
        var scale = referenceHeight / Screen.height;
        return new(
            bottom.x * scale,
            (Screen.height - top.y) * scale,
            (top.x - bottom.x) * scale,
            (top.y - bottom.y) * scale
        );
    }
}
