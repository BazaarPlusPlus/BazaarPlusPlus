#nullable enable
using BazaarPlusPlus.Core.Cards;
using BazaarPlusPlus.Core.Ui;
using BazaarPlusPlus.Infrastructure.UiTokens;
using UnityEngine;
using UnityEngine.UI;

namespace BazaarPlusPlus.Game.Touch;

/// <summary>Shared framed portrait geometry and async binding for Collection and reusable History rows.</summary>
internal sealed class TouchPortraitView
{
    private readonly Image _image;
    private readonly GameObject _fallback;
    private int _binding;

    public TouchPortraitView(RectTransform icon, TouchNativeSkin skin, TouchMetrics metrics)
    {
        var frame = TouchUiKit.Rect(
            "PortraitFrame",
            icon,
            Vector2.zero,
            Vector2.one,
            Vector2.zero,
            Vector2.zero
        );
        skin.Tag(frame);
        var viewport = TouchUiKit.Rect(
            "PortraitViewport",
            icon,
            Vector2.zero,
            Vector2.one,
            Vector2.one * metrics.Dp(2f),
            Vector2.one * -metrics.Dp(2f)
        );
        TouchUiKit.Fill(viewport, InGameTheme.Panel, false);
        viewport.gameObject.AddComponent<RectMask2D>();
        var fallback = TouchUiKit.Rect(
            "Group",
            icon,
            Vector2.zero,
            Vector2.one,
            Vector2.zero,
            Vector2.zero
        );
        for (var index = 0; index < 4; index++)
        {
            var dot = TouchUiKit.Fixed(
                "Member",
                fallback,
                new(0.5f, 0.5f),
                new(
                    (index % 2 == 0 ? -1f : 1f) * metrics.Dp(5f),
                    (index < 2 ? -1f : 1f) * metrics.Dp(5f)
                ),
                Vector2.one * metrics.Dp(6f)
            );
            TouchUiKit.Fill(dot, TouchUiKit.MutedTextColor, false);
        }
        var art = TouchUiKit.Fixed(
            "PortraitArt",
            viewport,
            new(0.5f, 0.5f),
            Vector2.zero,
            viewport.rect.size
        );
        var image = TouchUiKit.Fill(art, Color.white, false);
        image.preserveAspect = false;
        image.enabled = false;
        _image = image;
        _fallback = fallback.gameObject;
    }

    public void Bind(Func<Task<Sprite?>>? load, CardHero? hero, Action<bool>? ready = null)
    {
        var binding = ++_binding;
        _image.enabled = false;
        _fallback.SetActive(true);
        ready?.Invoke(false);
        if (load == null)
            return;
        _ = Load(load, hero, binding, ready);
    }

    private async Task Load(
        Func<Task<Sprite?>> load,
        CardHero? hero,
        int binding,
        Action<bool>? ready
    )
    {
        var sprite = await load();
        // Archive rows are reused for other heroes while a native request is in flight.
        if (_image == null || binding != _binding || sprite == null)
            return;
        _image.sprite = sprite;
        FramePortrait(_image, sprite, hero);
        _image.enabled = true;
        _fallback.SetActive(false);
        ready?.Invoke(true);
    }

    // Sprite vertices describe the visible mesh in local units; the pivot converts them back
    // into sprite-rect pixels. The Image keeps responsibility for packed/rotated atlas UVs.
    private static void FramePortrait(Image image, Sprite sprite, CardHero? hero)
    {
        var source = sprite.rect.size;
        var min = source;
        var max = Vector2.zero;
        foreach (var vertex in sprite.vertices)
        {
            var pixel = vertex * sprite.pixelsPerUnit + sprite.pivot;
            min = Vector2.Min(min, pixel);
            max = Vector2.Max(max, pixel);
        }
        var visible = max - min;
        if (visible.x <= 0f || visible.y <= 0f)
        {
            min = Vector2.zero;
            max = source;
            visible = source;
        }
        var frame = ((RectTransform)image.rectTransform.parent).rect.size;
        var scale = Math.Min(frame.x / visible.x, frame.y / visible.y);
        // These default hero portraits support an upper-body crop. Dooley's antennae,
        // multi-character heroes and encounter compositions keep their complete silhouette.
        var zoom = hero switch
        {
            CardHero.Vanessa or CardHero.Pygmalien => 1.4f,
            _ => 0.94f,
        };
        scale *= zoom;
        var center = (min + max) * 0.5f;
        var position = (source * 0.5f - center) * scale;
        if (zoom > 1f)
            position.y = frame.y * 0.46f - (max.y - source.y * 0.5f) * scale;
        image.rectTransform.sizeDelta = source * scale;
        image.rectTransform.anchoredPosition = position;
    }
}
