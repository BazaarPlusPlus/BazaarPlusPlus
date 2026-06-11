#nullable enable
using BazaarPlusPlus.Infrastructure;
using UnityEngine;
using UnityEngine.UI;

namespace BazaarPlusPlus.Game.Settings;

internal interface IBppNativeSettingsButtonCloneOwner;

internal static class BppNativeSettingsButtonClone
{
    private const string LogCategory = "BppNativeSettingsButtonClone";

    internal static RectTransform? FindOrCreate(
        Button anchorButton,
        BppSettingsDockPlacement placement
    )
    {
        if (anchorButton == null)
            return null;

        var hostRect = anchorButton.transform.parent as RectTransform;
        if (hostRect == null)
            return null;

        var existing = hostRect.Find(placement.DockButtonObjectName) as RectTransform;
        if (existing != null)
        {
            ConfigureRect(existing, anchorButton.transform as RectTransform);
            BppDockButtonVisuals.Apply(
                existing.gameObject,
                placement.ButtonIconKind,
                explicitIcon: null,
                freshClone: false
            );
            return existing;
        }

        var cloneObject = Object.Instantiate(
            anchorButton.gameObject,
            hostRect,
            worldPositionStays: false
        );
        cloneObject.name = placement.DockButtonObjectName;

        var nativeIcon = BppDockButtonVisuals.ResolveNativeIconImage(cloneObject);
        StripNativeButtonBehavior(cloneObject);
        BppDockButtonVisuals.Apply(
            cloneObject,
            placement.ButtonIconKind,
            nativeIcon,
            freshClone: true
        );

        var rect = cloneObject.GetComponent<RectTransform>();
        if (rect == null)
            return null;

        ConfigureRect(rect, anchorButton.transform as RectTransform);

        BppLog.Debug(
            LogCategory,
            $"Clone '{placement.Key}': hasCloneOwner={HasCloneOwner(cloneObject)}, "
                + $"hasBazaarButtonController={cloneObject.GetComponent<BazaarButtonController>() != null}, "
                + $"hasButtonCustom={cloneObject.GetComponent<ButtonCustom>() != null}, "
                + $"localScale={rect.localScale}, lossyScale={rect.lossyScale}"
        );

        return rect;
    }

    private static void ConfigureRect(RectTransform rectTransform, RectTransform? anchorRect)
    {
        rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
        rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        rectTransform.pivot = new Vector2(0.5f, 0.5f);
        rectTransform.localRotation = Quaternion.identity;

        if (anchorRect == null)
            return;

        var anchorSize = anchorRect.rect.size;
        if (anchorSize.x > 0.0001f && anchorSize.y > 0.0001f)
            rectTransform.sizeDelta = anchorSize;
    }

    private static void StripNativeButtonBehavior(GameObject cloneObject)
    {
        foreach (var custom in cloneObject.GetComponentsInChildren<ButtonCustom>(true))
            Object.DestroyImmediate(custom);

        foreach (var native in cloneObject.GetComponentsInChildren<BazaarButtonController>(true))
            Object.DestroyImmediate(native);

        foreach (var owner in cloneObject.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (owner is IBppNativeSettingsButtonCloneOwner)
                Object.DestroyImmediate(owner);
        }

        foreach (var nestedButton in cloneObject.GetComponentsInChildren<Button>(true))
        {
            if (nestedButton.gameObject != cloneObject)
                Object.DestroyImmediate(nestedButton);
        }

        var button = cloneObject.GetComponent<Button>() ?? cloneObject.AddComponent<Button>();
        var targetGraphic = cloneObject.GetComponent<Image>();
        if (targetGraphic == null)
        {
            targetGraphic = cloneObject.AddComponent<Image>();
            targetGraphic.color = new Color(1f, 1f, 1f, 0f);
        }

        targetGraphic.raycastTarget = true;
        button.onClick.RemoveAllListeners();
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        button.interactable = true;
        button.targetGraphic = targetGraphic;
    }

    private static bool HasCloneOwner(GameObject cloneObject)
    {
        foreach (var owner in cloneObject.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (owner is IBppNativeSettingsButtonCloneOwner)
                return true;
        }

        return false;
    }
}
