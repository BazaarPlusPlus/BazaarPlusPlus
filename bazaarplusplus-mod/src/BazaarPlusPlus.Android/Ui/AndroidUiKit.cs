using Il2CppTMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace BazaarPlusPlus.Android.Ui;

/// <summary>
/// uGUI building blocks over the IL2CPP proxies. Geometry is in the canvas's reference units
/// (<see cref="ReferenceResolution"/>, scaled by screen height); text uses the game's own font,
/// borrowed from a live label, so BPP ships no font of its own.
/// </summary>
internal static class AndroidUiKit
{
    public static readonly Vector2 ReferenceResolution = new(1920f, 1080f);

    public static readonly Color PanelColor = new(0.07f, 0.06f, 0.05f, 0.96f);
    public static readonly Color DimColor = new(0f, 0f, 0f, 0.55f);
    public static readonly Color ButtonColor = new(0.25f, 0.19f, 0.12f, 1f);
    public static readonly Color ActiveColor = new(0.55f, 0.38f, 0.14f, 1f);
    public static readonly Color TextColor = new(0.96f, 0.91f, 0.8f, 1f);
    public static readonly Color MutedTextColor = new(0.72f, 0.66f, 0.56f, 1f);

    public static GameObject CreateOverlayCanvas(string name, int sortingOrder)
    {
        var root = new GameObject(name);
        UnityEngine.Object.DontDestroyOnLoad(root);
        var canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = sortingOrder;
        var scaler = root.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = ReferenceResolution;
        scaler.matchWidthOrHeight = 1f;
        root.AddComponent<GraphicRaycaster>();
        return root;
    }

    /// <summary>A child rect anchored to a fraction of its parent, inset by pixel offsets.</summary>
    public static RectTransform Rect(
        string name,
        Transform parent,
        Vector2 anchorMin,
        Vector2 anchorMax,
        Vector2 offsetMin,
        Vector2 offsetMax
    )
    {
        var go = new GameObject(name);
        var rect = go.AddComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = offsetMin;
        rect.offsetMax = offsetMax;
        return rect;
    }

    /// <summary>A rect of fixed size whose pivot sits at <paramref name="anchor"/>.</summary>
    public static RectTransform Fixed(
        string name,
        Transform parent,
        Vector2 anchor,
        Vector2 position,
        Vector2 size
    )
    {
        var rect = Rect(name, parent, anchor, anchor, Vector2.zero, Vector2.zero);
        rect.pivot = anchor;
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        return rect;
    }

    public static Image Fill(RectTransform rect, Color color, bool blocksTouches = true)
    {
        var image = rect.gameObject.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = blocksTouches;
        return image;
    }

    public static TextMeshProUGUI Text(
        RectTransform rect,
        TMP_FontAsset font,
        string text,
        float size,
        TextAlignmentOptions alignment,
        Color color
    )
    {
        var label = rect.gameObject.AddComponent<TextMeshProUGUI>();
        label.font = font;
        label.fontSize = size;
        label.alignment = alignment;
        label.color = color;
        label.raycastTarget = false;
        label.enableWordWrapping = true;
        label.richText = true;
        label.text = text;
        return label;
    }

    /// <summary>A filled button with a centered label; <paramref name="onTap"/> runs on release.</summary>
    public static (Button Button, Image Background, TextMeshProUGUI Label) Button(
        RectTransform rect,
        TMP_FontAsset font,
        string text,
        float size,
        Action onTap
    )
    {
        var background = Fill(rect, ButtonColor);
        var button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = background;
        button.onClick.AddListener((UnityAction)onTap);
        var labelRect = Rect(
            "Label",
            rect,
            Vector2.zero,
            Vector2.one,
            new(16f, 4f),
            new(-16f, -4f)
        );
        var label = Text(labelRect, font, text, size, TextAlignmentOptions.Center, TextColor);
        return (button, background, label);
    }
}
