using BazaarPlusPlus.Core.Ui;
using BazaarPlusPlus.Game.HistoryPanel.Data;
using BazaarPlusPlus.Localization;
using Il2CppTMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BazaarPlusPlus.Android.Ui;

/// <summary>
/// The History panel's uGUI parts on the touch layout: its palette (the desktop History's), its
/// labels, buttons wired through <see cref="AndroidUiActions.OnTap"/> so every handler runs in the
/// main-thread scope, drag-scrolled lists with inertia, and the single-line or multi-line text
/// input. Rects are <see cref="TouchRect"/>s from Core's <c>HistoryTouchLayout</c>, top-down from
/// their parent's top-left corner.
/// </summary>
internal static class AndroidHistoryUi
{
    public static AndroidNativeSkin Skin { get; private set; } = null!;

    public static void RefreshSkin() => Skin = new AndroidNativeSkin();

    public static readonly Color Ink = new(0.96f, 0.89f, 0.73f, 1f);
    public static readonly Color Muted = new(0.69f, 0.63f, 0.53f, 1f);
    public static readonly Color ButtonFill = new(0.13f, 0.09f, 0.055f, 1f);
    public static readonly Color ButtonDimmed = new(0.09f, 0.065f, 0.045f, 1f);
    public static readonly Color Backdrop = new(0.055f, 0.038f, 0.026f, 1f);
    public static readonly Color PanelFill = new(0.09f, 0.07f, 0.05f, 1f);
    public static readonly Color Shade = new(0f, 0f, 0f, 0.8f);
    public static readonly Color InputFill = new(0.20f, 0.15f, 0.09f, 1f);
    public static readonly Color Win = new(0.62f, 0.80f, 0.54f, 1f);
    public static readonly Color Loss = new(0.90f, 0.40f, 0.31f, 1f);
    public static readonly Color Accent = new(0.86f, 0.66f, 0.30f, 1f);

    /// <summary>A label; it takes no touches.</summary>
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
        var label = AndroidUiKit.Text(
            AndroidTouchScreen.Child(name, parent, area),
            font,
            "",
            size,
            alignment,
            color ?? Ink
        );
        Shape(label, wrap);
        return label;
    }

    /// <summary>A label filling a fraction of its parent: x, y, width, height from the top-left.</summary>
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
        var rect = AndroidUiKit.Rect(
            "Label",
            parent,
            new Vector2(x, 1f - y - height),
            new Vector2(x + width, 1f - y),
            Vector2.zero,
            Vector2.zero
        );
        var label = AndroidUiKit.Text(rect, font, "", size, alignment, color ?? Ink);
        Shape(label, wrap);
        return label;
    }

    /// <summary>
    /// A filled button with a centred label. <paramref name="control"/> names it in the
    /// main-thread scope's error reports.
    /// </summary>
    public static HistoryButton Button(
        Transform parent,
        TMP_FontAsset font,
        string control,
        TouchRect area,
        float size,
        Action onTap,
        string text = ""
    ) => Button(AndroidTouchScreen.Child(control, parent, area), font, control, size, onTap, text);

    public static HistoryButton Button(
        RectTransform rect,
        TMP_FontAsset font,
        string control,
        float size,
        Action onTap,
        string text = ""
    )
    {
        var fill = AndroidUiKit.Fill(rect, ButtonFill);
        Skin.Select(fill, false);
        var button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = fill;
        AndroidUiActions.OnTap(button, "history." + control, onTap);
        var labelRect = AndroidUiKit.Rect(
            "Label",
            rect,
            Vector2.zero,
            Vector2.one,
            new Vector2(size * 0.4f, 2f),
            new Vector2(-size * 0.4f, -2f)
        );
        var label = AndroidUiKit.Text(
            labelRect,
            font,
            text,
            size,
            TextAlignmentOptions.Center,
            Ink
        );
        Shape(label, wrap: true);
        return new HistoryButton(button, fill, label, Skin);
    }

    /// <summary>
    /// A tap area over a rect, transparent unless <paramref name="fill"/> colours it: it raycasts,
    /// so it takes the tap and a drag that starts on it still scrolls the list behind it. It is
    /// the rect's only Graphic, since a GameObject holds one.
    /// </summary>
    public static Button TapArea(
        RectTransform rect,
        string control,
        Action onTap,
        Color? fill = null
    )
    {
        var hit = AndroidUiKit.Fill(rect, fill ?? new Color(0f, 0f, 0f, 0f));
        var button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = hit;
        button.transition = Selectable.Transition.None;
        AndroidUiActions.OnTap(button, "history." + control, onTap);
        return button;
    }

    /// <summary>A full-parent shade that swallows taps and calls <paramref name="onOutsideTap"/>.</summary>
    public static RectTransform Overlay(
        Transform parent,
        string name,
        string control,
        Action onOutsideTap
    )
    {
        var root = AndroidUiKit.Rect(
            name,
            parent,
            Vector2.zero,
            Vector2.one,
            Vector2.zero,
            Vector2.zero
        );
        var shade = AndroidUiKit.Fill(root, Shade);
        var button = root.gameObject.AddComponent<Button>();
        button.targetGraphic = shade;
        button.transition = Selectable.Transition.None;
        AndroidUiActions.OnTap(button, "history." + control, onOutsideTap);
        return root;
    }

    /// <summary>An opaque panel that swallows taps, so they do not reach the shade behind it.</summary>
    public static RectTransform Panel(Transform parent, string name, TouchRect area)
    {
        var rect = AndroidTouchScreen.Child(name, parent, area);
        Skin.Frame(rect, PanelFill, borderOnly: true);
        var swallow = rect.gameObject.AddComponent<Button>();
        swallow.transition = Selectable.Transition.None;
        return rect;
    }

    /// <summary>
    /// A drag-scrolled list in <paramref name="area"/> with inertia, its content anchored to the
    /// viewport's top-left corner so rows are placed top-down from it.
    /// </summary>
    public static (ScrollRect Scroll, RectTransform Content) Scroll(
        Transform parent,
        string name,
        TouchRect area,
        bool horizontal
    )
    {
        var rect = AndroidTouchScreen.Child(name, parent, area);
        var viewport = AndroidUiKit.Rect(
            "Viewport",
            rect,
            Vector2.zero,
            Vector2.one,
            Vector2.zero,
            Vector2.zero
        );
        // Transparent but raycast: a drag anywhere in the list scrolls it.
        AndroidUiKit.Fill(viewport, new Color(0f, 0f, 0f, 0.001f));
        viewport.gameObject.AddComponent<RectMask2D>();
        var content = AndroidUiKit.Rect(
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

    /// <summary>
    /// A text input: one line for a code, several for a note. Its value is read when the user
    /// submits; a caller that follows edits (the note's count) adds its listener through
    /// <see cref="AndroidUiActions.OnChanged"/>.
    /// </summary>
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
        var rect = AndroidTouchScreen.Child(name, parent, area);
        Skin.Frame(rect, InputFill, borderOnly: true);
        var background = rect.GetComponent<Image>();
        var viewport = AndroidUiKit.Rect(
            "TextViewport",
            rect,
            Vector2.zero,
            Vector2.one,
            new Vector2(size * 0.5f, size * 0.3f),
            new Vector2(-size * 0.5f, -size * 0.3f)
        );
        viewport.gameObject.AddComponent<RectMask2D>();
        var alignment = multiLine ? TextAlignmentOptions.TopLeft : TextAlignmentOptions.MidlineLeft;
        var text = AndroidUiKit.Text(
            AndroidUiKit.Rect(
                "Text",
                viewport,
                Vector2.zero,
                Vector2.one,
                Vector2.zero,
                Vector2.zero
            ),
            font,
            "",
            size,
            alignment,
            Ink
        );
        var hint = AndroidUiKit.Text(
            AndroidUiKit.Rect(
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
            StatusSeverity.Success => new Color(0.48f, 0.76f, 0.42f, 1f),
            StatusSeverity.Confirm => Ink,
            _ => Muted,
        };

    public static string T(string english, string chinese) =>
        L.Resolve(new LocalizedTextSet(english, chinese));

    public static void SetActive(GameObject? target, bool active)
    {
        if (target != null && target.activeSelf != active)
            target.SetActive(active);
    }

    // MEMORY gotcha (desktop): NoWrap + Ellipsis at a scaled font can drop a whole TMP block,
    // so a wrapped label truncates instead.
    private static void Shape(TextMeshProUGUI label, bool wrap)
    {
        label.textWrappingMode = wrap ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
        label.overflowMode = TextOverflowModes.Ellipsis;
    }
}

/// <summary>
/// A History button and its parts. A dimmed button stays tappable: the model says why it cannot
/// act, and the tap puts that reason on the status line (tap-to-explain), as hover does on the
/// desktop. A tab draws only its selected state, so the tabs read apart from the actions; a
/// danger button's label carries the loss colour.
/// </summary>
internal sealed class HistoryButton(
    Button button,
    Image fill,
    TextMeshProUGUI label,
    AndroidNativeSkin skin
)
{
    private (string Text, bool Selected, bool Enabled, bool Dimmed)? _shown;
    private Image[]? _chevronStrokes;
    private HistoryButtonStyle _style;

    public Button Button { get; } = button;

    public Image Fill { get; } = fill;

    public TextMeshProUGUI Label { get; } = label;

    public GameObject GameObject => Button.gameObject;

    /// <summary>Restyles the button; the next <see cref="Set"/> draws it.</summary>
    public HistoryButton As(HistoryButtonStyle style)
    {
        _style = style;
        _shown = null;
        return this;
    }

    /// <summary>A centred navigation chevron; the button keeps its descriptive label and action.</summary>
    public void UseChevron(float angle, float size)
    {
        Label.gameObject.SetActive(false);
        var icon = AndroidUiKit.Fixed(
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
            var stroke = AndroidUiKit.Fixed(
                "Stroke",
                icon,
                new(0.5f, 0.5f),
                new(0f, side * size * 0.2f),
                new(size * 0.68f, size * 0.15f)
            );
            stroke.localRotation = Quaternion.Euler(0f, 0f, -side * 45f);
            _chevronStrokes[index] = AndroidUiKit.Fill(stroke, Label.color, false);
        }
    }

    /// <param name="text">The label.</param>
    /// <param name="selected">True: it reads as the current choice.</param>
    /// <param name="enabled">False: the button takes no taps.</param>
    /// <param name="dimmed">True: it reads as unavailable but still takes the tap.</param>
    public void Set(string text, bool selected = false, bool enabled = true, bool dimmed = false)
    {
        if (_shown == (text, selected, enabled, dimmed))
            return;
        _shown = (text, selected, enabled, dimmed);
        Label.text = text;
        Button.interactable = enabled;
        var muted = dimmed || !enabled;
        skin.Select(Fill, selected, muted);
        var quietTab = _style == HistoryButtonStyle.Tab && !selected;
        // Transparent, not disabled: the fill still takes the tap.
        if (quietTab)
            Fill.color = Color.clear;
        Label.color =
            muted || quietTab ? AndroidHistoryUi.Muted
            : _style == HistoryButtonStyle.Danger ? AndroidHistoryUi.Loss
            : AndroidHistoryUi.Ink;
        if (_chevronStrokes != null)
            foreach (var stroke in _chevronStrokes)
                stroke.color = Label.color;
    }
}

internal enum HistoryButtonStyle
{
    Action,
    Tab,
    Danger,
}
