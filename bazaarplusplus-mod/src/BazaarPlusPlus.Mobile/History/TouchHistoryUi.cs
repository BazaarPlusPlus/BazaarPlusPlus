using BazaarPlusPlus.Core.Ui;
using BazaarPlusPlus.Game.HistoryPanel.Data;
using BazaarPlusPlus.Mobile.Ui;
using UnityEngine;
using UnityEngine.UI;

namespace BazaarPlusPlus.Mobile.History;

internal static class TouchHistoryUi
{
    public static NativeSkin Skin { get; private set; } = null!;
    private static Sprite? _normalArt,
        _selectedArt;
    private static float _artPixelsPerUnit;

    public static void UseArtwork(Sprite? normal, Sprite? selected, float pixelsPerUnit)
    {
        _normalArt = normal;
        _selectedArt = selected;
        _artPixelsPerUnit = pixelsPerUnit;
    }

    public static void RefreshSkin() => Skin = new NativeSkin();

    public static readonly Color Ink = InGameTheme.Ink;
    public static readonly Color Muted = InGameTheme.InkMuted;
    public static readonly Color ButtonFill = InGameTheme.Button;
    public static readonly Color ButtonDimmed = InGameTheme.ButtonDimmed;
    public static readonly Color Backdrop = InGameTheme.Backdrop;
    public static readonly Color Shade = InGameTheme.Scrim;
    public static readonly Color InputFill = InGameTheme.Input;
    public static readonly Color Win = InGameTheme.Success;
    public static readonly Color Loss = InGameTheme.Failure;
    public static readonly Color Accent = InGameTheme.Accent;

    public static Image ControlSurface(RectTransform rect)
    {
        var image = UiKit.Fill(rect, _normalArt != null ? Color.white : ButtonFill, false);
        image.sprite = _normalArt;
        image.type = Image.Type.Sliced;
        image.pixelsPerUnitMultiplier = _artPixelsPerUnit;
        return image;
    }

    public static TextMeshProUGUI Label(
        Transform parent,
        TMP_FontAsset font,
        string name,
        TouchRect area,
        float size,
        TextAlignmentOptions alignment = TextAlignmentOptions.MidlineLeft,
        Color? color = null,
        bool wrap = false
    )
    {
        var label = UiKit.Text(
            TouchScreen.Child(name, parent, area),
            font,
            "",
            size,
            alignment,
            color ?? Ink
        );
        Shape(label, wrap);
        return label;
    }

    public static TextMeshProUGUI Fraction(
        Transform parent,
        TMP_FontAsset font,
        float x,
        float y,
        float width,
        float height,
        float size,
        TextAlignmentOptions alignment = TextAlignmentOptions.MidlineLeft,
        Color? color = null,
        bool wrap = false
    )
    {
        var rect = UiKit.Rect(
            "Label",
            parent,
            new Vector2(x, 1f - y - height),
            new Vector2(x + width, 1f - y),
            Vector2.zero,
            Vector2.zero
        );
        var label = UiKit.Text(rect, font, "", size, alignment, color ?? Ink);
        Shape(label, wrap);
        return label;
    }

    public static HistoryButton Button(
        Transform parent,
        TMP_FontAsset font,
        string control,
        TouchRect area,
        float size,
        Action onTap,
        string text = ""
    ) => Button(TouchScreen.Child(control, parent, area), font, control, size, onTap, text);

    public static HistoryButton Button(
        RectTransform rect,
        TMP_FontAsset font,
        string control,
        float size,
        Action onTap,
        string text = ""
    )
    {
        var fill = UiKit.Fill(rect, ButtonFill);
        Skin.Select(fill, false);
        var button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = fill;
        UiActions.OnTap(button, "history." + control, onTap);
        var labelRect = UiKit.Rect(
            "Label",
            rect,
            Vector2.zero,
            Vector2.one,
            new Vector2(size * 0.4f, 2f),
            new Vector2(-size * 0.4f, -2f)
        );
        var label = UiKit.Text(labelRect, font, text, size, TextAlignmentOptions.Center, Ink);
        Shape(label, wrap: true);
        var controlButton = new HistoryButton(button, fill, label, Skin).WithHeaderArt(
            _normalArt,
            _selectedArt,
            _artPixelsPerUnit
        );
        controlButton.Set(text);
        return controlButton;
    }

    // A GameObject holds one Graphic. Use it for the raycast so a drag still reaches the ScrollRect.
    public static Button TapArea(
        RectTransform rect,
        string control,
        Action onTap,
        Color? fill = null
    )
    {
        var hit = fill is { } color ? UiKit.Fill(rect, color) : UiKit.HitArea(rect);
        var button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = hit;
        button.transition = Selectable.Transition.None;
        UiActions.OnTap(button, "history." + control, onTap);
        return button;
    }

    public static RectTransform Overlay(
        Transform parent,
        string name,
        string control,
        Action onOutsideTap
    )
    {
        var root = UiKit.Rect(name, parent, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        var shade = UiKit.Fill(root, Shade);
        var button = root.gameObject.AddComponent<Button>();
        button.targetGraphic = shade;
        button.transition = Selectable.Transition.None;
        UiActions.OnTap(button, "history." + control, onOutsideTap);
        return root;
    }

    /// <summary>An opaque panel that swallows taps, so they do not reach the shade behind it.</summary>
    public static RectTransform Panel(Transform parent, string name, TouchRect area)
    {
        var rect = TouchScreen.Child(name, parent, area);
        Skin.Frame(rect, InGameTheme.Dialog, borderOnly: true);
        var swallow = rect.gameObject.AddComponent<Button>();
        swallow.transition = Selectable.Transition.None;
        return rect;
    }

    public static (ScrollRect Scroll, RectTransform Content) Scroll(
        Transform parent,
        string name,
        TouchRect area,
        bool horizontal
    )
    {
        var rect = TouchScreen.Child(name, parent, area);
        var viewport = UiKit.Rect(
            "Viewport",
            rect,
            Vector2.zero,
            Vector2.one,
            Vector2.zero,
            Vector2.zero
        );
        // Transparent but raycast: a drag anywhere in the list scrolls it.
        UiKit.HitArea(viewport);
        viewport.gameObject.AddComponent<RectMask2D>();
        var content = UiKit.Rect(
            "Content",
            viewport,
            new Vector2(0f, 1f),
            new Vector2(0f, 1f),
            Vector2.zero,
            Vector2.zero
        );
        content.pivot = new Vector2(0f, 1f);
        content.sizeDelta = new Vector2(area.Width, area.Height);
        var scroll = rect.gameObject.AddComponent<ScrollRect>();
        scroll.viewport = viewport;
        scroll.content = content;
        scroll.horizontal = horizontal;
        scroll.vertical = !horizontal;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.inertia = true;
        scroll.decelerationRate = 0.12f;
        scroll.scrollSensitivity = 40f;
        return (scroll, content);
    }

    public static TMP_InputField Input(
        Transform parent,
        TMP_FontAsset font,
        string name,
        TouchRect area,
        float size,
        string placeholder,
        int characterLimit,
        bool multiLine
    )
    {
        var rect = TouchScreen.Child(name, parent, area);
        Skin.Frame(rect, InputFill, borderOnly: true);
        var background = rect.GetComponent<Image>();
        var viewport = UiKit.Rect(
            "TextViewport",
            rect,
            Vector2.zero,
            Vector2.one,
            new Vector2(size * 0.5f, size * 0.3f),
            new Vector2(-size * 0.5f, -size * 0.3f)
        );
        viewport.gameObject.AddComponent<RectMask2D>();
        var alignment = multiLine ? TextAlignmentOptions.TopLeft : TextAlignmentOptions.MidlineLeft;
        var text = UiKit.Text(
            UiKit.Rect("Text", viewport, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero),
            font,
            "",
            size,
            alignment,
            Ink
        );
        var hint = UiKit.Text(
            UiKit.Rect(
                "Placeholder",
                viewport,
                Vector2.zero,
                Vector2.one,
                Vector2.zero,
                Vector2.zero
            ),
            font,
            placeholder,
            size,
            alignment,
            Muted
        );
        var input = rect.gameObject.AddComponent<TMP_InputField>();
        input.targetGraphic = background;
        input.textViewport = viewport;
        input.textComponent = text;
        input.placeholder = hint;
        input.fontAsset = font;
        input.pointSize = size;
        input.lineType = multiLine
            ? TMP_InputField.LineType.MultiLineNewline
            : TMP_InputField.LineType.SingleLine;
        input.characterLimit = characterLimit;
        return input;
    }

    public static Color StatusColor(StatusSeverity severity) =>
        severity switch
        {
            StatusSeverity.Failure => Loss,
            StatusSeverity.Success => InGameTheme.Success,
            StatusSeverity.Confirm => Ink,
            _ => Muted,
        };

    public static void SetActive(GameObject? target, bool active)
    {
        if (target != null && target.activeSelf != active)
            target.SetActive(active);
    }

    // TMP's NoWrap with Ellipsis can drop an entire scaled text block. Wrap and truncate instead.
    private static void Shape(TextMeshProUGUI label, bool wrap)
    {
        label.textWrappingMode = wrap ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
        label.overflowMode = TextOverflowModes.Ellipsis;
    }
}

// Dimmed buttons still accept taps so they can explain why an action is unavailable.
internal sealed class HistoryButton(
    Button button,
    Image fill,
    TextMeshProUGUI label,
    NativeSkin skin
)
{
    private (string Text, bool Selected, bool Enabled, bool Dimmed)? _shown;
    private Image[]? _chevronStrokes;
    private HistoryButtonStyle _style;
    private Sprite? _headerNormal;
    private Sprite? _headerSelected;
    private float _headerPixelsPerUnit;

    public Button Button { get; } = button;

    public Image Fill { get; } = fill;

    public TextMeshProUGUI Label { get; } = label;

    public GameObject GameObject => Button.gameObject;

    public HistoryButton As(HistoryButtonStyle style)
    {
        _style = style;
        _shown = null;
        return this;
    }

    public HistoryButton WithHeaderArt(Sprite? normal, Sprite? selected, float pixelsPerUnit)
    {
        _headerNormal = normal;
        _headerSelected = selected;
        _headerPixelsPerUnit = pixelsPerUnit;
        _shown = null;
        return this;
    }

    public void UseChevron(float angle, float size)
    {
        Label.gameObject.SetActive(false);
        var icon = UiKit.Fixed(
            "Chevron",
            Button.transform,
            new(0.5f, 0.5f),
            Vector2.zero,
            Vector2.one * size
        );
        icon.localRotation = Quaternion.Euler(0f, 0f, angle);
        _chevronStrokes = new Image[2];
        for (var index = 0; index < _chevronStrokes.Length; index++)
        {
            var side = index == 0 ? -1f : 1f;
            var stroke = UiKit.Fixed(
                "Stroke",
                icon,
                new(0.5f, 0.5f),
                new(0f, side * size * 0.2f),
                new(size * 0.68f, size * 0.15f)
            );
            stroke.localRotation = Quaternion.Euler(0f, 0f, -side * 45f);
            _chevronStrokes[index] = UiKit.Fill(stroke, Label.color, false);
        }
    }

    public void UseTrailingChevron(float size)
    {
        UseChevron(-90f, size);
        Label.gameObject.SetActive(true);
        var icon = (RectTransform)Button.transform.Find("Chevron");
        icon.anchorMin = icon.anchorMax = new Vector2(.92f, .5f);
        Label.margin = new Vector4(size, 0, 2f * size, 0);
    }

    public void Set(string text, bool selected = false, bool enabled = true, bool dimmed = false)
    {
        if (_shown == (text, selected, enabled, dimmed))
            return;
        _shown = (text, selected, enabled, dimmed);
        Label.text = text;
        Button.interactable = enabled;
        var muted = dimmed || !enabled;
        skin.Select(Fill, selected, muted);
        if (_headerNormal != null)
        {
            Fill.sprite = selected ? _headerSelected ?? _headerNormal : _headerNormal;
            Fill.type = Image.Type.Sliced;
            Fill.pixelsPerUnitMultiplier = _headerPixelsPerUnit;
            Fill.color = muted ? InGameTheme.WithAlpha(Color.white, .5f) : Color.white;
        }
        var quietTab = _style == HistoryButtonStyle.Tab && !selected;
        var restingRecord = _style == HistoryButtonStyle.Record && !selected;
        var colors = Button.colors;
        colors.normalColor =
            quietTab ? Color.clear
            : restingRecord ? InGameTheme.TintRest
            : Color.white;
        colors.highlightedColor =
            quietTab ? InGameTheme.TintQuietHover
            : restingRecord ? InGameTheme.TintRestHover
            : Color.white;
        colors.pressedColor = InGameTheme.TintPressed;
        colors.selectedColor = colors.normalColor;
        colors.disabledColor = quietTab ? Color.clear : InGameTheme.TintDisabled;
        Button.colors = colors;
        Label.color =
            muted ? TouchHistoryUi.Muted
            : quietTab || restingRecord ? InGameTheme.InkSoft
            : _style == HistoryButtonStyle.Danger ? TouchHistoryUi.Loss
            : TouchHistoryUi.Ink;
        if (_chevronStrokes != null)
            foreach (var stroke in _chevronStrokes)
                stroke.color = Label.color;
    }
}

internal enum HistoryButtonStyle
{
    Action,
    Tab,
    Record,
    Danger,
}
