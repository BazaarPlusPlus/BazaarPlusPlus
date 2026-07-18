#nullable enable
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
    private static readonly Color CollectionHover = new(0.62f, 0.86f, 1f, 1f);

    internal static BppDockButtonColorSpec ResolveColors()
    {
        var hover = CollectionHover;
        return new BppDockButtonColorSpec(
            normal: Color.white,
            highlighted: hover,
            pressed: Color.Lerp(hover, Color.black, 0.18f),
            selected: hover,
            disabled: new Color(1f, 1f, 1f, 0.34f),
            fadeDuration: 0.08f
        );
    }

    internal static BppDockButtonVisualState ResolveButtonState(
        BppDockButtonVisualState? nativeState
    )
    {
        if (nativeState.HasValue)
            return nativeState.Value;

        var spec = ResolveColors();
        return BppDockButtonVisualState.Capture(
            Selectable.Transition.ColorTint,
            new ColorBlock
            {
                normalColor = spec.Normal,
                highlightedColor = spec.Highlighted,
                pressedColor = spec.Pressed,
                selectedColor = spec.Selected,
                disabledColor = spec.Disabled,
                colorMultiplier = 1f,
                fadeDuration = spec.FadeDuration,
            },
            new SpriteState(),
            new AnimationTriggers()
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
        Image? explicitIcon,
        bool freshClone,
        BppDockButtonVisualState? nativeState
    )
    {
        if (cloneObject == null)
            return;

        var frame = cloneObject.GetComponent<Image>() ?? cloneObject.AddComponent<Image>();
        var sprite = BppDockButtonSpriteProvider.Get();
        var icon = explicitIcon ?? FindMarkedIconImage(cloneObject) ?? FindIconImage(cloneObject);
        if (sprite != null && icon != null)
            ApplyIcon(icon, sprite);

        frame.raycastTarget = true;

        if (freshClone)
            DisableUnusedChildRaycasts(cloneObject.transform);

        var button = cloneObject.GetComponent<Button>() ?? cloneObject.AddComponent<Button>();
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        button.interactable = true;
        var resolvedState = ResolveButtonState(nativeState);
        resolvedState.ApplyTo(button, frame);

        var nativeVisualState =
            button.GetComponent<BppDockButtonNativeVisualState>()
            ?? button.gameObject.AddComponent<BppDockButtonNativeVisualState>();
        nativeVisualState.Initialize(button, resolvedState);
    }

    private static Image? FindMarkedIconImage(GameObject cloneObject)
    {
        foreach (var image in cloneObject.GetComponentsInChildren<Image>(includeInactive: true))
        {
            if (!image.gameObject.name.Equals(IconObjectName, StringComparison.Ordinal))
                continue;

            return image;
        }

        return null;
    }

    private static Image? FindIconImage(GameObject cloneObject)
    {
        Image? best = null;
        var bestArea = float.MaxValue;
        foreach (var image in cloneObject.GetComponentsInChildren<Image>(includeInactive: true))
        {
            if (image.gameObject == cloneObject)
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

    internal static void ApplyIcon(Image icon, Sprite sprite)
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
            foreach (var graphic in child.GetComponentsInChildren<Graphic>(includeInactive: true))
                graphic.raycastTarget = false;
        }
    }
}
