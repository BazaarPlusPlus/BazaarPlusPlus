using BazaarPlusPlus.Core.Ui;
using BazaarPlusPlus.Mobile.Ui;
using UnityEngine;
using UnityEngine.UI;

namespace BazaarPlusPlus.Mobile.Hud;

/// <summary>Native circular artwork inside an independent, full-sized touch target.</summary>
internal static class TouchRoundControl
{
    /// <summary>
    /// <paramref name="art"/> drawn at <paramref name="size"/>, the native button's own size,
    /// centred in <paramref name="target"/>; <paramref name="icon"/> sits where the native icon
    /// does. A tap runs <paramref name="action"/> as the control <paramref name="control"/>.
    /// </summary>
    public static Button Create(
        RectTransform target,
        NativeRoundArt art,
        Sprite? icon,
        Vector2 size,
        string control,
        Action action
    )
    {
        var hit = UiKit.Fill(target, new Color(0, 0, 0, .001f));
        var visual = UiKit.Fixed("NativeFrame", target, new(.5f, .5f), Vector2.zero, size);
        Image? surface = null;
        foreach (var layer in art.Layers)
        {
            var layerImage = UiKit.Fill(
                Part("Art", visual, layer.Bounds, size),
                layer.Color,
                false
            );
            layerImage.sprite = layer.Sprite;
            surface ??= layerImage;
        }
        var side = Math.Min(size.x, size.y) * .56f;
        var glyph = art.Icon is { } place
            ? Part("Icon", visual, place, size)
            : UiKit.Fixed("Icon", visual, new(.5f, .5f), Vector2.zero, new(side, side));
        var image = UiKit.Fill(glyph, Color.white, false);
        image.sprite = icon;
        image.enabled = icon != null;
        image.preserveAspect = true;
        var button = target.gameObject.AddComponent<Button>();
        button.targetGraphic = surface ?? hit;
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        UiActions.OnTap(button, control, action);
        return button;
    }

    private static RectTransform Part(
        string name,
        RectTransform visual,
        TouchRect area,
        Vector2 size
    ) =>
        TouchScreen.Child(
            name,
            visual,
            new(area.Left * size.x, area.Top * size.y, area.Width * size.x, area.Height * size.y)
        );
}
