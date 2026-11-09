#nullable enable
using BazaarPlusPlus.GameInterop.Fonts;
using BazaarPlusPlus.GameInterop.NativeSkin;
using BazaarPlusPlus.Infrastructure.UiTokens;
using UnityEngine;
using UnityEngine.UI;

namespace BazaarPlusPlus.Infrastructure.DesktopKit;

/// <summary>A text level of the kit: size and colour only, since the game's font has one weight.</summary>
internal enum KitTextRole
{
    Title,
    Section,
    Body,
    Meta,
    Caption,
}

internal enum KitSize
{
    Md,
    Sm,
}

internal enum KitIcon
{
    Close,
    ChevronLeft,
    ChevronRight,
    ChevronDown,
    Check,
}

/// <summary>
/// The Desktop UI kit (ADR-0027, design/components/InGameKit/README.md): one instance per view
/// builds its controls on the game's plate shape, tinted by the in-game tokens, and repaints the
/// plates when the shape arrives. Controls size themselves through uGUI layout (a
/// <see cref="LayoutElement"/> and, for labelled controls, a layout group); a consumer places
/// them in its own rects or layout groups. Call <see cref="Tick"/> every frame the view shows.
/// </summary>
internal sealed class DesktopKit
{
    // Label side padding of md and sm buttons, and the stroke of the kit's drawn icons.
    private const float PaddingMd = 18f;
    private const float PaddingSm = 13f;
    private const float IconStroke = 2f;

    private readonly NativeGameTypography.OwnedTextPreparation _font;
    private readonly List<Image> _plates = new();
    private int _shapeGeneration = -1;

    internal DesktopKit(NativeGameTypography.OwnedTextPreparation font) => _font = font;

    /// <summary>Asks for the plate shape and repaints every plate when it changes.</summary>
    internal void Tick()
    {
        DesktopNativeSkin.RequestShape();
        if (_shapeGeneration == DesktopNativeSkin.Generation)
            return;
        _shapeGeneration = DesktopNativeSkin.Generation;
        _plates.RemoveAll(plate => plate == null);
        foreach (var plate in _plates)
            Shape(plate);
    }

    internal static float Height(KitSize size) =>
        size == KitSize.Md ? InGameSizes.ControlMd : InGameSizes.ControlSm;

    internal static float FontSize(KitTextRole role) =>
        role switch
        {
            KitTextRole.Title => InGameSizes.TextTitle,
            KitTextRole.Section => InGameSizes.TextSection,
            KitTextRole.Body => InGameSizes.TextBody,
            KitTextRole.Meta => InGameSizes.TextMeta,
            _ => InGameSizes.TextCaption,
        };

    internal static Color Ink(KitTextRole role) =>
        role is KitTextRole.Meta or KitTextRole.Caption ? InGameTheme.InkMuted : InGameTheme.Ink;

    // ---- primitives ----

    internal static RectTransform Rect(Transform parent, string name)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        return rect;
    }

    /// <summary>Stretches <paramref name="rect"/> over its parent, inset on every side.</summary>
    internal static RectTransform Fill(RectTransform rect, float inset = 0f)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(inset, inset);
        rect.offsetMax = new Vector2(-inset, -inset);
        return rect;
    }

    /// <summary>Gives a rect fixed layout sizes; a negative value leaves that axis free.</summary>
    internal static LayoutElement Size(Component target, float width, float height)
    {
        var element = target.GetComponent<LayoutElement>();
        if (element == null)
            element = target.gameObject.AddComponent<LayoutElement>();
        if (width >= 0)
            element.minWidth = element.preferredWidth = width;
        if (height >= 0)
            element.minHeight = element.preferredHeight = height;
        return element;
    }

    /// <summary>A plate of the kit's shape over its parent, outside layout, in one colour.</summary>
    internal Image Plate(Transform parent, Color color, string name = "Plate")
    {
        var image = Fill(Rect(parent, name)).gameObject.AddComponent<Image>();
        image.raycastTarget = false;
        image.color = color;
        image.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        image.transform.SetAsFirstSibling();
        Shape(image);
        _plates.Add(image);
        return image;
    }

    // The shape, sliced; until it loads (or when its slice is empty) the plate is a plain
    // rectangle in the same colour.
    private static void Shape(Image image)
    {
        var sprite = DesktopNativeSkin.Shape;
        image.sprite = sprite;
        image.type =
            sprite != null && sprite.border != Vector4.zero ? Image.Type.Sliced : Image.Type.Simple;
        image.pixelsPerUnitMultiplier = DesktopNativeSkin.ShapePixelsPerUnit;
    }

    internal TextMeshProUGUI Text(
        Transform parent,
        string text,
        KitTextRole role,
        Color? color = null,
        TextAlignmentOptions alignment = TextAlignmentOptions.MidlineLeft
    )
    {
        var label = Rect(parent, "Text").gameObject.AddComponent<TextMeshProUGUI>();
        _font.Apply(label);
        label.text = text;
        label.fontSize = FontSize(role);
        label.color = color ?? Ink(role);
        label.alignment = alignment;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.overflowMode = TextOverflowModes.Ellipsis;
        label.raycastTarget = false;
        return label;
    }

    /// <summary>A 1px <c>ingame-rule</c> line, as wide as its parent's layout gives it.</summary>
    internal static Image Hairline(Transform parent, float alpha)
    {
        var line = Rect(parent, "Hairline").gameObject.AddComponent<Image>();
        line.color = InGameTheme.WithAlpha(InGameTheme.Rule, alpha);
        line.raycastTarget = false;
        Size(line, -1, 1);
        return line;
    }

    /// <summary>An icon drawn from strokes, in <paramref name="color"/>.</summary>
    internal static RectTransform Icon(Transform parent, KitIcon icon, float size, Color color)
    {
        var box = Rect(parent, $"Icon{icon}");
        Size(box, size, size);
        void Stroke(float length, Vector2 pivot, Vector2 at, float angle)
        {
            var bar = Rect(box, "Stroke");
            bar.anchorMin = bar.anchorMax = new Vector2(.5f, .5f);
            bar.pivot = pivot;
            bar.sizeDelta = new Vector2(length, IconStroke);
            bar.anchoredPosition = at;
            bar.localRotation = Quaternion.Euler(0, 0, angle);
            var image = bar.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
        }
        var half = new Vector2(.5f, .5f);
        switch (icon)
        {
            case KitIcon.Close:
                Stroke(size * 1.15f, half, Vector2.zero, 45);
                Stroke(size * 1.15f, half, Vector2.zero, -45);
                break;
            case KitIcon.Check:
                Stroke(
                    size * .42f,
                    new Vector2(1, .5f),
                    new Vector2(-size * .12f, -size * .22f),
                    -45
                );
                Stroke(
                    size * .8f,
                    new Vector2(0, .5f),
                    new Vector2(-size * .12f, -size * .22f),
                    50
                );
                break;
            default:
                var turn = icon switch
                {
                    KitIcon.ChevronLeft => 180f,
                    KitIcon.ChevronDown => -90f,
                    _ => 0f,
                };
                box.localRotation = Quaternion.Euler(0, 0, turn);
                var tip = new Vector2(size * .2f, 0);
                Stroke(size * .55f, new Vector2(1, .5f), tip, -45);
                Stroke(size * .55f, new Vector2(1, .5f), tip, 45);
                break;
        }
        return box;
    }

    // ---- buttons ----

    internal KitButton Button(
        Transform parent,
        string label,
        KitButtonKind kind,
        KitSize size,
        Action onClick
    )
    {
        var (button, row, plate) = ButtonShell(parent, size, onClick, "Button");
        var text = Text(
            row,
            label,
            size == KitSize.Md ? KitTextRole.Body : KitTextRole.Meta,
            alignment: TextAlignmentOptions.Center
        );
        button.Bind(kind, plate, new Graphic[] { text });
        return button;
    }

    /// <summary>A square button with a drawn icon, such as a panel's close.</summary>
    internal KitButton IconButton(
        Transform parent,
        KitIcon icon,
        KitButtonKind kind,
        KitSize size,
        Action onClick
    )
    {
        var (button, row, plate) = ButtonShell(parent, size, onClick, $"{icon}Button");
        var group = row.GetComponent<HorizontalLayoutGroup>();
        group.padding = new RectOffset(0, 0, 0, 0);
        Size(button, Height(size), Height(size));
        var glyph = Icon(row, icon, size == KitSize.Md ? 14 : 12, InGameTheme.Ink);
        button.Bind(kind, plate, glyph.GetComponentsInChildren<Graphic>());
        return button;
    }

    /// <summary>A button holding a label and icons side by side, such as a pager arrow.</summary>
    internal KitButton IconLabelButton(
        Transform parent,
        string label,
        KitIcon? before,
        KitIcon? after,
        KitButtonKind kind,
        KitSize size,
        Action onClick
    )
    {
        var (button, row, plate) = ButtonShell(parent, size, onClick, "Button");
        var ink = new List<Graphic>();
        var iconSize = size == KitSize.Md ? 10 : 9;
        if (before is { } first)
            ink.AddRange(
                Icon(row, first, iconSize, InGameTheme.Ink).GetComponentsInChildren<Graphic>()
            );
        if (label.Length > 0)
            ink.Add(
                Text(
                    row,
                    label,
                    size == KitSize.Md ? KitTextRole.Body : KitTextRole.Meta,
                    alignment: TextAlignmentOptions.Center
                )
            );
        if (after is { } last)
            ink.AddRange(
                Icon(row, last, iconSize, InGameTheme.Ink).GetComponentsInChildren<Graphic>()
            );
        button.Bind(kind, plate, ink.ToArray());
        return button;
    }

    private (KitButton Button, RectTransform Row, Image Plate) ButtonShell(
        Transform parent,
        KitSize size,
        Action onClick,
        string name
    )
    {
        var rect = Rect(parent, name);
        var button = rect.gameObject.AddComponent<KitButton>();
        var plate = Plate(rect, Color.clear);
        plate.raycastTarget = true;
        var group = rect.gameObject.AddComponent<HorizontalLayoutGroup>();
        var padding = (int)(size == KitSize.Md ? PaddingMd : PaddingSm);
        group.padding = new RectOffset(padding, padding, 0, 0);
        group.spacing = InGameSizes.Space6;
        group.childAlignment = TextAnchor.MiddleCenter;
        group.childControlWidth = group.childControlHeight = true;
        group.childForceExpandWidth = group.childForceExpandHeight = false;
        Size(rect, -1, Height(size));
        button.onClick.AddListener(() => onClick());
        return (button, rect, plate);
    }

    // ---- selection controls ----

    /// <summary>
    /// A segmented control: one <c>ingame-button</c> track, and only the selected option gets a
    /// plate. The track is a control height and its options sit inside it.
    /// </summary>
    internal (RectTransform Track, KitButton[] Options) Segmented(
        Transform parent,
        IReadOnlyList<string> labels,
        KitSize size,
        Action<int> onSelect
    )
    {
        var track = Rect(parent, "Segmented");
        Plate(track, InGameTheme.Button, "Track");
        var inner = size == KitSize.Md ? InGameSizes.ControlSm : InGameSizes.BadgeHeight;
        var inset = (int)((Height(size) - inner) / 2);
        var group = track.gameObject.AddComponent<HorizontalLayoutGroup>();
        group.padding = new RectOffset(inset, inset, inset, inset);
        group.spacing = InGameSizes.Space2;
        group.childControlWidth = group.childControlHeight = true;
        group.childForceExpandWidth = true;
        group.childForceExpandHeight = false;
        Size(track, -1, Height(size));
        var options = new KitButton[labels.Count];
        for (var index = 0; index < labels.Count; index++)
        {
            var option = index;
            options[index] = Button(
                track,
                labels[index],
                KitButtonKind.Segment,
                KitSize.Sm,
                () => onSelect(option)
            );
            Size(options[index], -1, inner);
            options[index].GetComponent<LayoutElement>().flexibleWidth = 1;
        }
        return (track, options);
    }

    internal KitButton Chip(Transform parent, string label, Action onClick)
    {
        var (button, row, plate) = ButtonShell(parent, KitSize.Sm, onClick, "Chip");
        var group = row.GetComponent<HorizontalLayoutGroup>();
        group.padding = new RectOffset((int)InGameSizes.Space12, (int)InGameSizes.Space12, 0, 0);
        Size(button, -1, InGameSizes.BadgeHeight);
        var text = Text(row, label, KitTextRole.Meta, alignment: TextAlignmentOptions.Center);
        button.Bind(KitButtonKind.Chip, plate, new Graphic[] { text });
        return button;
    }

    /// <summary>A checkbox and its label; the label keeps <c>ingame-ink</c>.</summary>
    internal KitButton Checkbox(Transform parent, string label, Action onClick)
    {
        var rect = Rect(parent, "Checkbox");
        var group = rect.gameObject.AddComponent<HorizontalLayoutGroup>();
        group.spacing = InGameSizes.Space8;
        group.childAlignment = TextAnchor.MiddleLeft;
        group.childControlWidth = group.childControlHeight = true;
        group.childForceExpandWidth = group.childForceExpandHeight = false;
        Size(rect, -1, InGameSizes.ControlSm);
        var box = Rect(rect, "Box");
        var boxSize = InGameSizes.BadgeHeight - InGameSizes.Space4;
        Size(box, boxSize, boxSize);
        var plate = Plate(box, InGameTheme.Chip);
        plate.raycastTarget = true;
        var mark = Icon(box, KitIcon.Check, boxSize * .6f, InGameTheme.Backdrop);
        mark.anchorMin = mark.anchorMax = new Vector2(.5f, .5f);
        mark.sizeDelta = new Vector2(boxSize * .6f, boxSize * .6f);
        mark.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        Text(rect, label, KitTextRole.Body);
        var button = rect.gameObject.AddComponent<KitButton>();
        button.onClick.AddListener(() => onClick());
        button.Bind(
            KitButtonKind.Check,
            plate,
            mark.GetComponentsInChildren<Graphic>(),
            mark.gameObject
        );
        return button;
    }

    // ---- fields ----

    /// <summary>A select: a sunk field showing the value, with a down chevron; pressing opens the consumer's menu.</summary>
    internal KitButton Select(Transform parent, string value, Action onClick)
    {
        var (button, row, plate) = ButtonShell(parent, KitSize.Md, onClick, "Select");
        var group = row.GetComponent<HorizontalLayoutGroup>();
        group.padding = new RectOffset((int)InGameSizes.Space12, (int)InGameSizes.Space12, 0, 0);
        group.childAlignment = TextAnchor.MiddleLeft;
        var text = Text(row, value, KitTextRole.Body);
        text.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
        Icon(row, KitIcon.ChevronDown, 10, InGameTheme.InkMuted);
        // A field is sunk: ingame-backdrop at rest, the row hover plate on top of it.
        var ground = Plate(button.transform, InGameTheme.Backdrop, "Ground");
        ground.transform.SetAsFirstSibling();
        button.Bind(KitButtonKind.Row, plate, Array.Empty<Graphic>());
        return button;
    }

    /// <summary>A sunk single-line text field with a placeholder.</summary>
    internal TMP_InputField Search(Transform parent, string placeholder)
    {
        var rect = Rect(parent, "Search");
        Size(rect, -1, InGameSizes.ControlMd);
        var ground = Plate(rect, InGameTheme.Backdrop, "Ground");
        ground.raycastTarget = true;
        var viewport = Fill(Rect(rect, "Viewport"));
        viewport.offsetMin = new Vector2(InGameSizes.Space12, 0);
        viewport.offsetMax = new Vector2(-InGameSizes.Space12, 0);
        viewport.gameObject.AddComponent<RectMask2D>();
        var text = Text(viewport, "", KitTextRole.Body);
        Fill(text.rectTransform);
        var hint = Text(viewport, placeholder, KitTextRole.Body, InGameTheme.InkMuted);
        Fill(hint.rectTransform);
        var input = rect.gameObject.AddComponent<TMP_InputField>();
        input.transition = Selectable.Transition.None;
        input.targetGraphic = ground;
        input.textViewport = viewport;
        input.textComponent = text;
        input.placeholder = hint;
        input.lineType = TMP_InputField.LineType.SingleLine;
        input.caretColor = InGameTheme.Ink;
        input.selectionColor = InGameTheme.WithAlpha(InGameTheme.Accent, .35f);
        return input;
    }

    // ---- content ----

    /// <summary>A section heading with an optional caption at its right, over a hairline.</summary>
    internal RectTransform SectionHeader(Transform parent, string title, string caption)
    {
        var block = Rect(parent, "SectionHeader");
        var column = block.gameObject.AddComponent<VerticalLayoutGroup>();
        column.spacing = InGameSizes.Space8;
        column.childControlWidth = column.childControlHeight = true;
        column.childForceExpandWidth = true;
        column.childForceExpandHeight = false;
        var line = Rect(block, "Line");
        var row = line.gameObject.AddComponent<HorizontalLayoutGroup>();
        row.childAlignment = TextAnchor.LowerLeft;
        row.childControlWidth = row.childControlHeight = true;
        row.childForceExpandWidth = row.childForceExpandHeight = false;
        Text(line, title, KitTextRole.Section)
            .gameObject.AddComponent<LayoutElement>()
            .flexibleWidth = 1;
        if (caption.Length > 0)
            Text(line, caption, KitTextRole.Caption, alignment: TextAlignmentOptions.BottomRight);
        Hairline(block, .4f);
        return block;
    }

    /// <summary>
    /// A selectable list row with no plate at rest; the consumer fills <paramref name="content"/>,
    /// a left-to-right row with the kit's padding. <see cref="Dividers"/> draws the hairlines.
    /// </summary>
    internal KitButton ListRow(
        Transform parent,
        float height,
        Action onClick,
        out RectTransform content
    )
    {
        var rect = Rect(parent, "ListRow");
        Size(rect, -1, height);
        var button = rect.gameObject.AddComponent<KitButton>();
        var plate = Plate(rect, Color.clear);
        plate.raycastTarget = true;
        var divider = Rect(rect, "Divider");
        divider.anchorMin = new Vector2(0, 1);
        divider.anchorMax = new Vector2(1, 1);
        divider.pivot = new Vector2(.5f, 1);
        divider.offsetMin = new Vector2(InGameSizes.Space12, -1);
        divider.offsetMax = new Vector2(-InGameSizes.Space12, 0);
        var line = divider.gameObject.AddComponent<Image>();
        line.color = InGameTheme.WithAlpha(InGameTheme.Rule, .22f);
        line.raycastTarget = false;
        divider.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        content = Fill(Rect(rect, "Content"));
        var group = content.gameObject.AddComponent<HorizontalLayoutGroup>();
        group.padding = new RectOffset(
            (int)InGameSizes.Space12,
            (int)InGameSizes.Space12,
            (int)InGameSizes.Space8,
            (int)InGameSizes.Space8
        );
        group.spacing = InGameSizes.Space12;
        group.childAlignment = TextAnchor.MiddleLeft;
        group.childControlWidth = group.childControlHeight = true;
        group.childForceExpandWidth = group.childForceExpandHeight = false;
        button.onClick.AddListener(() => onClick());
        button.Bind(KitButtonKind.Row, plate, Array.Empty<Graphic>());
        return button;
    }

    /// <summary>
    /// Hairlines between rows: none above the first row, and none touching a selected row's plate.
    /// </summary>
    internal static void Dividers(IReadOnlyList<KitButton> rows)
    {
        for (var index = 0; index < rows.Count; index++)
        {
            var hidden = index == 0 || rows[index].On || rows[index - 1].On;
            rows[index].transform.Find("Divider")?.gameObject.SetActive(!hidden);
        }
    }

    /// <summary>A two-line text column: a Body title over a Meta line, which may hold rich text.</summary>
    internal RectTransform TitleAndMeta(Transform parent, string title, string meta)
    {
        var column = Rect(parent, "TitleAndMeta");
        var group = column.gameObject.AddComponent<VerticalLayoutGroup>();
        group.spacing = InGameSizes.Space2;
        group.childAlignment = TextAnchor.MiddleLeft;
        group.childControlWidth = group.childControlHeight = true;
        group.childForceExpandWidth = true;
        group.childForceExpandHeight = false;
        Text(column, title, KitTextRole.Body);
        var line = Text(column, meta, KitTextRole.Meta);
        line.richText = true;
        column.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
        return column;
    }

    /// <summary>A square image of native art, such as a portrait or a badge, at a fixed size.</summary>
    internal static Image Art(Transform parent, Sprite? sprite, float width, float height)
    {
        var image = Rect(parent, "Art").gameObject.AddComponent<Image>();
        image.sprite = sprite;
        image.preserveAspect = true;
        image.color = sprite != null ? Color.white : InGameTheme.ArtFallback;
        image.raycastTarget = false;
        Size(image, width, height);
        return image;
    }

    /// <summary>
    /// The day strip: one <c>ingame-button</c> track holding a run's battles left to right.
    /// Add days with <see cref="Day"/>.
    /// </summary>
    internal RectTransform DayStrip(Transform parent)
    {
        var track = Rect(parent, "DayStrip");
        Plate(track, InGameTheme.Button, "Track");
        var group = track.gameObject.AddComponent<HorizontalLayoutGroup>();
        var inset = (int)InGameSizes.Space4;
        group.padding = new RectOffset(inset, inset, inset, inset);
        group.spacing = InGameSizes.Space4;
        group.childControlWidth = group.childControlHeight = true;
        group.childForceExpandWidth = true;
        group.childForceExpandHeight = true;
        return track;
    }

    /// <summary>One battle in the day strip: the day, the opponent's portrait and the outcome word.</summary>
    internal KitButton Day(
        Transform strip,
        string day,
        Sprite? portrait,
        string outcome,
        Color outcomeColor,
        Action onClick
    )
    {
        var rect = Rect(strip, "Day");
        var button = rect.gameObject.AddComponent<KitButton>();
        var plate = Plate(rect, Color.clear);
        plate.raycastTarget = true;
        var column = rect.gameObject.AddComponent<VerticalLayoutGroup>();
        column.padding = new RectOffset(0, 0, (int)InGameSizes.Space6, (int)InGameSizes.Space6);
        column.spacing = InGameSizes.Space4;
        column.childAlignment = TextAnchor.MiddleCenter;
        column.childControlWidth = column.childControlHeight = true;
        column.childForceExpandWidth = column.childForceExpandHeight = false;
        var dayText = Text(rect, day, KitTextRole.Caption, alignment: TextAlignmentOptions.Center);
        Art(rect, portrait, 34, 34);
        Text(rect, outcome, KitTextRole.Meta, outcomeColor, TextAlignmentOptions.Center);
        button.onClick.AddListener(() => onClick());
        button.Bind(KitButtonKind.Day, plate, new Graphic[] { dayText });
        return button;
    }

    /// <summary>
    /// A floating surface (a tooltip or a dialog card): <c>ingame-dialog</c> one unit inside a
    /// plate of <c>ingame-rule</c>, so it keeps an edge over the scrim. Its children go in the
    /// returned rect, laid out top to bottom with <paramref name="padding"/>.
    /// </summary>
    internal RectTransform Floating(Transform parent, string name, float padding)
    {
        var rect = Rect(parent, name);
        Plate(rect, InGameTheme.WithAlpha(InGameTheme.Rule, .55f), "Edge");
        var edge = rect.GetChild(0);
        var inner = Plate(rect, InGameTheme.Dialog, "Ground");
        Fill(inner.rectTransform, 1);
        edge.SetAsFirstSibling();
        var group = rect.gameObject.AddComponent<VerticalLayoutGroup>();
        var pad = (int)padding;
        group.padding = new RectOffset(pad, pad, pad, pad);
        group.spacing = InGameSizes.Space8;
        group.childControlWidth = group.childControlHeight = true;
        group.childForceExpandWidth = true;
        group.childForceExpandHeight = false;
        return rect;
    }
}
