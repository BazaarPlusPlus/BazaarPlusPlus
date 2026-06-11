#nullable enable
using System;
using UnityEngine;
using UnityEngine.UI;

namespace BazaarPlusPlus.Game.Settings;

internal readonly struct BppDockButtonColorSpec(
    Color normal,
    Color highlighted,
    Color pressed,
    Color selected,
    Color disabled,
    float fadeDuration
)
{
    internal Color Normal { get; } = normal;
    internal Color Highlighted { get; } = highlighted;
    internal Color Pressed { get; } = pressed;
    internal Color Selected { get; } = selected;
    internal Color Disabled { get; } = disabled;
    internal float FadeDuration { get; } = fadeDuration;
}

internal static class BppDockButtonVisuals
{
    private const string IconObjectName = "BPP_DockButtonIcon";
    private const string SettingsPanelPrefix = "BPP_SettingsDockPanel_";

    private static readonly Color SettingsHover = new(1f, 0.9f, 0.58f, 1f);
    private static readonly Color CollectionHover = new(0.62f, 0.86f, 1f, 1f);

    internal static BppDockButtonColorSpec ResolveColors(BppDockButtonIconKind kind)
    {
        var hover = kind == BppDockButtonIconKind.CollectionPanel ? CollectionHover : SettingsHover;
        return new BppDockButtonColorSpec(
            normal: Color.white,
            highlighted: hover,
            pressed: Color.Lerp(hover, Color.black, 0.18f),
            selected: hover,
            disabled: new Color(1f, 1f, 1f, 0.34f),
            fadeDuration: 0.08f
        );
    }

    internal static Image? ResolveNativeIconImage(GameObject cloneObject)
    {
        return cloneObject
            .GetComponentInChildren<BazaarButtonController>(includeInactive: true)
            ?.ButtonIcon;
    }

    internal static void Apply(
        GameObject cloneObject,
        BppDockButtonIconKind kind,
        Image? explicitIcon,
        bool freshClone
    )
    {
        if (cloneObject == null)
            return;

        var frame = cloneObject.GetComponent<Image>() ?? cloneObject.AddComponent<Image>();
        var sprite = BppDockButtonSpriteProvider.Get(kind);
        var icon = explicitIcon ?? FindMarkedIconImage(cloneObject) ?? FindIconImage(cloneObject);
        if (sprite != null && icon != null)
            ApplyIcon(icon, sprite);

        frame.raycastTarget = true;

        if (freshClone)
            DisableUnusedChildRaycasts(cloneObject.transform);

        var button = cloneObject.GetComponent<Button>() ?? cloneObject.AddComponent<Button>();
        button.targetGraphic = frame;
        button.transition = Selectable.Transition.ColorTint;
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        button.interactable = true;

        var spec = ResolveColors(kind);
        button.colors = new ColorBlock
        {
            normalColor = spec.Normal,
            highlightedColor = spec.Highlighted,
            pressedColor = spec.Pressed,
            selectedColor = spec.Selected,
            disabledColor = spec.Disabled,
            colorMultiplier = 1f,
            fadeDuration = spec.FadeDuration,
        };
    }

    private static Image? FindMarkedIconImage(GameObject cloneObject)
    {
        var root = cloneObject.transform;
        foreach (var image in cloneObject.GetComponentsInChildren<Image>(includeInactive: true))
        {
            if (!image.gameObject.name.Equals(IconObjectName, StringComparison.Ordinal))
                continue;

            if (IsInsideSettingsPanel(image.transform, root))
                continue;

            return image;
        }

        return null;
    }

    private static Image? FindIconImage(GameObject cloneObject)
    {
        Image? best = null;
        var bestArea = float.MaxValue;
        var root = cloneObject.transform;
        foreach (var image in cloneObject.GetComponentsInChildren<Image>(includeInactive: true))
        {
            if (image.gameObject == cloneObject)
                continue;

            if (IsInsideSettingsPanel(image.transform, root))
                continue;

            if (image.transform is not RectTransform rect)
                continue;

            var size = rect.rect.size;
            var area = Mathf.Abs(size.x * size.y);
            if (area <= 0.0001f || area >= bestArea)
                continue;

            best = image;
            bestArea = area;
        }

        return best;
    }

    private static bool IsInsideSettingsPanel(Transform candidate, Transform cloneRoot)
    {
        var current = candidate;
        while (current != null && current != cloneRoot)
        {
            if (current.name.StartsWith(SettingsPanelPrefix, StringComparison.Ordinal))
                return true;

            current = current.parent;
        }

        return false;
    }

    private static void ApplyIcon(Image icon, Sprite sprite)
    {
        icon.gameObject.name = IconObjectName;
        icon.enabled = true;
        icon.sprite = sprite;
        icon.type = Image.Type.Simple;
        icon.preserveAspect = true;
        icon.color = Color.white;
        icon.raycastTarget = false;
    }

    private static void DisableUnusedChildRaycasts(Transform root)
    {
        for (var index = 0; index < root.childCount; index++)
        {
            var child = root.GetChild(index);
            if (child.name.StartsWith(SettingsPanelPrefix, StringComparison.Ordinal))
                continue;

            foreach (var graphic in child.GetComponentsInChildren<Graphic>(includeInactive: true))
                graphic.raycastTarget = false;
        }
    }
}
