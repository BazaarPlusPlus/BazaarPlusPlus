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

    public static readonly Color PanelColor = InGameTheme.Backdrop;
    public static readonly Color ButtonColor = InGameTheme.Chip;
    public static readonly Color ActiveColor = InGameTheme.ChipActive;
    public static readonly Color TextColor = InGameTheme.Ink;
    public static readonly Color MutedTextColor = InGameTheme.InkMuted;

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

    /// <summary>
    /// A filled button with a centered label <paramref name="labelInset"/> in from either side
    /// (<c>TouchChrome.LabelInset</c>, so the padding is the same dp on every screen);
    /// <paramref name="onTap"/> runs on release.
    /// </summary>
    public static (Button Button, Image Background, TextMeshProUGUI Label) Button(
        RectTransform rect,
        TMP_FontAsset font,
        string text,
        float size,
        float labelInset,
        Action onTap
    )
    {
        var background = Fill(rect, ButtonColor);
        var button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = background;
        button.onClick.AddListener((UnityAction)onTap);
        var labelRect = Rect("Label", rect, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        InsetLabel(labelRect, labelInset);
        var label = Text(labelRect, font, text, size, TextAlignmentOptions.Center, TextColor);
        return (button, background, label);
    }

    /// <summary>Pads a button's label rect <paramref name="inset"/> in from either side.</summary>
    public static void InsetLabel(RectTransform label, float inset)
    {
        label.offsetMin = new Vector2(inset, 0f);
        label.offsetMax = new Vector2(-inset, 0f);
    }

    /// <summary>The close glyph every BPP sheet, dialog, and menu shows, History's included.</summary>
    public const string CloseGlyph = "X";

    /// <summary>
    /// The close button every BPP surface shares: <see cref="CloseGlyph"/> at title size in the
    /// rect its layout gives it (<c>TouchChrome.CloseAt</c>, one touch target tall).
    /// </summary>
    public static (Button Button, Image Background, TextMeshProUGUI Label) CloseButton(
        RectTransform rect,
        TMP_FontAsset font,
        float titleFont,
        float labelInset,
        Action onTap
    ) => Button(rect, font, CloseGlyph, titleFont, labelInset, onTap);

    /// <summary>
    /// A vertical drag-scrolled list filling <paramref name="area"/>, with inertia; its content
    /// stretches across the viewport and hangs from its top, so rows are placed top-down.
    /// </summary>
    public static (ScrollRect Scroll, RectTransform Content) VerticalScroll(RectTransform area)
    {
        var viewport = Rect(
            "Viewport",
            area,
            Vector2.zero,
            Vector2.one,
            Vector2.zero,
            Vector2.zero
        );
        // Transparent but raycast: a drag anywhere in the list scrolls it.
        Fill(viewport, new Color(0f, 0f, 0f, 0.001f));
        viewport.gameObject.AddComponent<RectMask2D>();
        var content = Rect(
            "Content",
            viewport,
            new(0f, 1f),
            new(1f, 1f),
            Vector2.zero,
            Vector2.zero
        );
        content.pivot = new Vector2(0f, 1f);
        var scroll = area.gameObject.AddComponent<ScrollRect>();
        scroll.viewport = viewport;
        scroll.content = content;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.inertia = true;
        scroll.decelerationRate = 0.12f;
        scroll.scrollSensitivity = 40f;
        return (scroll, content);
    }
}
