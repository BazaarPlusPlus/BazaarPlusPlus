using BazaarPlusPlus.Core.Ui;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using UnityEngine.UI;

namespace BazaarPlusPlus.Android.GameInterop;

internal static class AndroidNativeHud
{
    private static Il2CppTheBazaar.PopupManager? _popup;
    private static float _nextPopupCheck;

    // One buffer for every read: main thread only, and GetWorldCorners overwrites all four.
    private static Il2CppStructArray<Vector3>? _corners;

    public static Il2CppTheBazaar.PopupManager? Popups
    {
        get
        {
            if (_popup != null)
                return _popup;
            var now = Time.unscaledTime;
            if (now < _nextPopupCheck)
                return null;
            _popup = UnityEngine.Object.FindFirstObjectByType<Il2CppTheBazaar.PopupManager>();
            if (_popup == null)
                _nextPopupCheck = now + 0.5f;
            return _popup;
        }
    }

    public static bool HasPopup =>
        Popups is { } popup && (popup.IsAnyPopupOpen || popup.IsSettingsOpen);

    public static bool IsInspectingCard =>
        Il2CppTheBazaar.Data.TooltipParentComponent?.CardTooltipController?.CurrentCard != null;

    // SceneLoader can finish transitioning while the gameplay loading screen still covers the live
    // settings button.
    public static bool IsChangingScene
    {
        get
        {
            try
            {
                if (Il2Cpp.SceneLoader.IsTransitioning)
                    return true;
                var loading =
                    Il2CppTheBazaar.Data.LoadingScreenController?.TryCast<Il2CppTheBazaar.LoadingScreenController>();
                return loading != null && loading.isActiveAndEnabled && loading.FadeAlpha < 1f;
            }
            catch
            {
                return false;
            }
        }
    }

    public static Button? FindSettingsButton(out bool inRun)
    {
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
