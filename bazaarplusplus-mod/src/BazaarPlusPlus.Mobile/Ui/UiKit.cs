using UnityEngine;
using UnityEngine.UI;

namespace BazaarPlusPlus.Mobile.Ui;

internal static class UiKit
{
    public static readonly Vector2 ReferenceResolution = new(1920f, 1080f);

    public const int SortingOrder = 30000;

    public static readonly Color PanelColor = InGameTheme.Backdrop;
    public static readonly Color ButtonColor = InGameTheme.Chip;
    public static readonly Color ActiveColor = InGameTheme.ChipActive;
    public static readonly Color TextColor = InGameTheme.Ink;
    public static readonly Color MutedTextColor = InGameTheme.InkMuted;

    /// <summary>The colour of <see cref="HitArea"/>: black at an alpha no display shows.</summary>
    public static readonly Color Invisible = InGameTheme.WithAlpha(Color.black, 0.001f);

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

    // A transparent Graphic still receives raycasts, which standalone tap and drag targets need.
    public static Image HitArea(RectTransform rect) => Fill(rect, Invisible);

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
        Il2CppUi.SetWordWrapping(label, true);
        label.richText = true;
        label.text = text;
        return label;
    }

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
        Il2CppUi.OnClick(button, onTap);
        var labelRect = Rect("Label", rect, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        InsetLabel(labelRect, labelInset);
        var label = Text(labelRect, font, text, size, TextAlignmentOptions.Center, TextColor);
        return (button, background, label);
    }

    public static void InsetLabel(RectTransform label, float inset)
    {
        label.offsetMin = new Vector2(inset, 0f);
        label.offsetMax = new Vector2(-inset, 0f);
    }

    public const string CloseGlyph = "X";

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
        HitArea(viewport);
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
