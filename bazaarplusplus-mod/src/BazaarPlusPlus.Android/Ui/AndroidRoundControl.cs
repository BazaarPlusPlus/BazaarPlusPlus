using BazaarPlusPlus.Android.GameInterop;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace BazaarPlusPlus.Android.Ui;

/// <summary>Native circular artwork inside an independent, full-sized touch target.</summary>
internal static class AndroidRoundControl
{
    public static (Button Button, Image Icon) Create(
        RectTransform target,
        IReadOnlyList<AndroidNativeArt.ButtonLayer> layers,
        Sprite? icon,
        float diameter,
        Action action
    )
    {
        var hit = AndroidUiKit.Fill(target, new Color(0, 0, 0, .001f));
        var visual = AndroidUiKit.Fixed(
            "NativeFrame",
            target,
            new(.5f, .5f),
            Vector2.zero,
            new(diameter, diameter)
        );
        Image? surface = null;
        foreach (var layer in layers)
        {
            var area = layer.Bounds;
            var rect = AndroidTouchScreen.Child(
                "Art",
                visual,
                new(
                    area.Left * diameter,
                    area.Top * diameter,
                    area.Width * diameter,
                    area.Height * diameter
                )
            );
            var layerImage = AndroidUiKit.Fill(rect, layer.Color, false);
            layerImage.sprite = layer.Sprite;
            surface ??= layerImage;
        }
        var glyph = AndroidUiKit.Fixed(
            "Icon",
            visual,
            new(.5f, .5f),
            Vector2.zero,
            new(diameter * .56f, diameter * .56f)
        );
        var image = AndroidUiKit.Fill(glyph, Color.white, false);
        image.sprite = icon;
        image.enabled = icon != null;
        image.preserveAspect = true;
        var button = target.gameObject.AddComponent<Button>();
        button.targetGraphic = surface ?? hit;
        button.onClick.AddListener((UnityAction)action);
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        return (button, image);
    }
}
