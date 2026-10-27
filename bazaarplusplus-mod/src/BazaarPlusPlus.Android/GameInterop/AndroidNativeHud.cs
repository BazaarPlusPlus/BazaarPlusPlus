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
                _popup = UnityEngine.Object.FindFirstObjectByType<Il2CppTheBazaar.PopupManager>();
            return _popup;
        }
    }

    public static bool HasPopup =>
        Popups is { } popup && (popup.IsAnyPopupOpen || popup.IsSettingsOpen);

    /// <summary>Whether the native card drawer is showing an inspected card.</summary>
    public static bool IsInspectingCard =>
        Il2CppTheBazaar.Data.TooltipParentComponent?.CardTooltipController?.CurrentCard != null;

    /// <summary>The active settings button; <paramref name="inRun"/> when it is the run's.</summary>
    public static Button? FindSettingsButton(out bool inRun)
    {
        // Scene objects only, inactive ones included: an asset is never active in a hierarchy.
        inRun = true;
        foreach (
            var fight in UnityEngine.Object.FindObjectsByType<Il2Cpp.FightMenuDialog>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.InstanceID
            )
        )
        {
            var button = fight.SettingButton?.GetButton();
            if (Active(button))
                return button;
        }
        inRun = false;
        foreach (
            var view in UnityEngine.Object.FindObjectsByType<Il2Cpp.SettingDialogsView>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.InstanceID
            )
        )
        {
            var mainMenu = view.MainMenuSettingOptionButton;
            if (Active(mainMenu))
                return mainMenu;
            var heroSelect = view.HeroSelectSettingOptionButton;
            if (Active(heroSelect))
                return heroSelect;
        }
        return null;
    }

    private static bool Active(Button? button) =>
        button != null && button.gameObject.activeInHierarchy;

    /// <summary>
    /// <paramref name="rect"/>'s screen rect, scaled to <paramref name="referenceHeight"/>; the
    /// caller resolves the rect's <paramref name="rootCanvas"/> once and keeps it.
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
