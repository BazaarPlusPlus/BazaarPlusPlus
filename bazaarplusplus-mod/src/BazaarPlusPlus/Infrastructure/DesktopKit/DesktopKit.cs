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
    Search,
    Heart,
}

/// <summary>A painted state mark of the kit, drawn where a trophy would be.</summary>
internal enum KitMark
{
    /// <summary>A finished run below the first trophy.</summary>
    Completed,
    Abandoned,
    Active,
    Won,
    Lost,
}

/// <summary>
/// The parts of a run row a consumer fills after <see cref="DesktopKit.RunRow"/>, and fills
/// again when it reuses the row for another run: the texts, the hero's portrait, the rank
/// badge and the mark.
/// </summary>
internal sealed class KitRunRow
{
    internal KitRunRow(
        KitButton button,
        TextMeshProUGUI title,
        TextMeshProUGUI meta,
        Image portrait,
        Image rank,
        Image mark
    )
    {
        Button = button;
        Title = title;
        Meta = meta;
        Portrait = portrait;
        Rank = rank;
        Mark = mark;
    }

    internal KitButton Button { get; }

    internal TextMeshProUGUI Title { get; }

    /// <summary>The meta line; it takes rich text.</summary>
    internal TextMeshProUGUI Meta { get; }

    /// <summary>The hero portrait inside the row's well, for <see cref="DesktopKit.ShowPortrait"/>.</summary>
    internal Image Portrait { get; }

    /// <summary>The rank badge; clear until <see cref="DesktopKit.Show"/> gives it a sprite.</summary>
    internal Image Rank { get; }

    /// <summary>The trophy or state mark.</summary>
    internal Image Mark { get; }
}

/// <summary>The parts of a day cell a consumer fills and refills (<see cref="DesktopKit.Day"/>).</summary>
internal sealed class KitDay
{
    internal KitDay(KitButton button, TextMeshProUGUI caption, Image portrait, Image mark)
    {
        Button = button;
        Caption = caption;
        Portrait = portrait;
        Mark = mark;
    }

    internal KitButton Button { get; }

    internal TextMeshProUGUI Caption { get; }

    /// <summary>The opponent's portrait, for <see cref="DesktopKit.ShowPortrait"/>.</summary>
    internal Image Portrait { get; }

    /// <summary>The outcome mark, for <see cref="DesktopKit.Show"/> with <see cref="DesktopKit.Mark"/>.</summary>
    internal Image Mark { get; }
}

/// <summary>
/// The day strip (<see cref="DesktopKit.DayStrip"/>): days go in <see cref="Days"/>; the strip
/// scrolls sideways when they do not fit.
/// </summary>
internal sealed class KitDayStrip
{
    internal KitDayStrip(RectTransform track, RectTransform days, ScrollRect scroll)
    {
        Track = track;
        Days = days;
        Scroll = scroll;
    }

    /// <summary>The strip itself, for the consumer's layout.</summary>
    internal RectTransform Track { get; }

    /// <summary>The parent of the day cells.</summary>
    internal RectTransform Days { get; }

    internal ScrollRect Scroll { get; }

    /// <summary>Scrolls so that <paramref name="day"/> is inside the strip.</summary>
    internal void Reveal(RectTransform day)
    {
        if (day == null || Scroll.viewport == null)
            return;
        Canvas.ForceUpdateCanvases();
        var view = Scroll.viewport.rect.width;
        var extra = Days.rect.width - view;
        if (extra <= 0)
            return;
        var left = day.anchoredPosition.x - day.rect.width * day.pivot.x;
        var offset = -Days.anchoredPosition.x;
        if (left < offset)
            offset = left;
        else if (left + day.rect.width > offset + view)
            offset = left + day.rect.width - view;
        Scroll.horizontalNormalizedPosition = Mathf.Clamp01(offset / extra);
    }
}

/// <summary>A modal layer of the kit (<see cref="DesktopKit.Modal"/>).</summary>
internal sealed class KitModal
{
    internal KitModal(GameObject root, RectTransform card)
    {
        Root = root;
        Card = card;
    }

    /// <summary>The scrim and everything on it; active while the modal shows.</summary>
    internal GameObject Root { get; }

    /// <summary>The card: its children go top to bottom.</summary>
    internal RectTransform Card { get; }

    internal bool Open => Root != null && Root.activeSelf;
}

/// <summary>Keeps a strip's content at least as wide as its viewport, and as wide as its days.</summary>
internal sealed class KitStripWidth : MonoBehaviour
{
    private void LateUpdate()
    {
        var content = (RectTransform)transform;
        if (content.parent is not RectTransform viewport)
            return;
        var extra = Mathf.Max(0f, LayoutUtility.GetPreferredWidth(content) - viewport.rect.width);
        if (!Mathf.Approximately(content.sizeDelta.x, extra))
            content.sizeDelta = new Vector2(extra, content.sizeDelta.y);
    }
}

/// <summary>A surface of the kit that is not a control.</summary>
internal enum KitSurface
{
    /// <summary>A rail or a well: the kit's ground.</summary>
    Ground,

    /// <summary>The track of a segmented control or the day strip: the brown centre, darkened.</summary>
    Track,

    /// <summary>A sunk field: the brown centre darkened, in a dimmed frame so it reads on a ground.</summary>
    Field,

    /// <summary>A tooltip or a dialog card: the ground inside the soft gold line.</summary>
    Floating,
}

/// <summary>
/// The Desktop UI kit (ADR-0027, design/components/InGameKit/README.md): one instance per view
/// builds its controls from the kit's art slots (BPP's plates, the game's rule and glyphs), and
/// repaints them when a sprite arrives. Controls size themselves through uGUI layout (a
/// <see cref="LayoutElement"/> and, for labelled controls, a layout group); a consumer places
/// them in its own rects or layout groups. Call <see cref="Tick"/> every frame the view shows.
/// </summary>
internal sealed class DesktopKit
{
    // Label side padding of md and sm buttons, the stroke of the kit's drawn icons, and the
    // drawn height of the game's divider rule.
    private const float PaddingMd = 18f;
    private const float PaddingSm = 13f;
    private const float IconStroke = 2f;
    private const float RuleHeight = 2f;

    // A run row: its portrait well, its rank and mark slots (one size, so the two sit side by
    // side on one line) and the fixed width of its result text, which keeps every row's marks on
    // one axis and every result on one right edge.
    internal const float RowPortrait = 44f;
    internal const float RowArt = 30f;

    // A day of the day strip: its portrait well and its outcome mark.
    private const float DayPortrait = 36f;
    private const float DayMark = 24f;

    // The strip's height: a day's caption, portrait and mark with the gaps and edges around them.
    private const float DayStripHeight = 100f;

    // Every day cell is this wide; a strip with more days than fit scrolls.
    private const float DayWidth = 76f;

    // How far one wheel step moves a kit list or strip.
    private const float ScrollSensitivity = 45f;

    // How far a list row's plate bleeds out of the row on each side. A row has no plate at
    // rest, so its content, not the hidden plate, sits on the column's edges: the portrait
    // under the section header, the result under the header's caption.
    private const float RowBleed = InGameSizes.Space12;

    // A paging arrow that is a panel's main control stands this high.
    private const float WideArrowHeight = 48f;

    // The source sprite each art image wants: a small copy arrives later and must not land
    // on an image that has since been given another sprite.
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<
        Image,
        System.Runtime.CompilerServices.StrongBox<Sprite?>
    > Wanted = new();

    // The portrait images whose medallion rises above its well.
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<
        Image,
        object
    > Risen = new();

    private readonly NativeGameTypography.OwnedTextPreparation _font;
    private readonly List<KitPlate> _plates = new();
    private readonly List<(Image Glyph, DesktopKitArt Art)> _glyphs = new();
    private readonly List<(Image Line, float Alpha)> _rules = new();
    private int _artGeneration = -1;

    internal DesktopKit(NativeGameTypography.OwnedTextPreparation font) => _font = font;

    /// <summary>Asks for the kit's art and repaints what it drew when a sprite changes.</summary>
    internal void Tick()
    {
        DesktopNativeSkin.RequestKitArt();
        if (_artGeneration == DesktopNativeSkin.Generation)
            return;
        _artGeneration = DesktopNativeSkin.Generation;
        _plates.RemoveAll(plate => plate.Image == null);
        foreach (var plate in _plates)
            plate.Refresh();
        _glyphs.RemoveAll(glyph => glyph.Glyph == null);
        foreach (var (glyph, art) in _glyphs)
            ShowGlyph(glyph, art);
        _rules.RemoveAll(rule => rule.Line == null);
        foreach (var (line, alpha) in _rules)
            ShowRule(line, alpha);
    }

    /// <summary>
    /// A button's label side padding. A quiet button has no plate at rest, so a row that ends
    /// in one pulls it out by this much: its label, not its hidden plate, sits on the row's edge.
    /// </summary>
    internal static float LabelInset(KitSize size) => size == KitSize.Md ? PaddingMd : PaddingSm;

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

    /// <summary>
    /// Gives a rect fixed layout sizes; a negative value leaves that axis free. Set a flexible
    /// size after this call, not before.
    /// </summary>
    internal static LayoutElement Size(Component target, float width, float height)
    {
        var element = target.GetComponent<LayoutElement>();
        if (element == null)
            element = target.gameObject.AddComponent<LayoutElement>();
        // A fixed axis takes no spare room, whatever the rect's own layout group could use.
        if (width >= 0)
        {
            element.minWidth = element.preferredWidth = width;
            element.flexibleWidth = 0;
        }
        if (height >= 0)
        {
            element.minHeight = element.preferredHeight = height;
            element.flexibleHeight = 0;
        }
        return element;
    }

    /// <summary>A plate over its parent, outside layout, showing one look.</summary>
    internal KitPlate Plate(Transform parent, KitLook look, string name = "Plate")
    {
        var image = Fill(Rect(parent, name)).gameObject.AddComponent<Image>();
        image.raycastTarget = false;
        image.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        image.transform.SetAsFirstSibling();
        var plate = new KitPlate(image, look);
        _plates.Add(plate);
        return plate;
    }

    /// <summary>A surface behind other content: a rail, a well, a track, a field or a card.</summary>
    internal KitPlate Surface(Transform parent, KitSurface surface) =>
        Plate(
            parent,
            surface switch
            {
                KitSurface.Track => KitPalette
                    .Brown(InGameTheme.Button)
                    .Times(InGameTheme.TintPressed),
                KitSurface.Field => new KitLook(
                    DesktopKitArt.Plate,
                    InGameTheme.ArtInputTint,
                    InGameTheme.Backdrop,
                    DesktopKitArt.Edge,
                    InGameTheme.ArtPanelTint
                ),
                KitSurface.Floating => new KitLook(
                    DesktopKitArt.Ground,
                    Color.white,
                    InGameTheme.Dialog,
                    DesktopKitArt.Edge,
                    Color.white
                ),
                _ => new KitLook(DesktopKitArt.Ground, Color.white, InGameTheme.Panel),
            },
            surface.ToString()
        );

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

    /// <summary>
    /// A rule as wide as its parent's layout gives it: the game's fading match-history divider,
    /// or a 1px <c>ingame-rule</c> line while that sprite is missing.
    /// </summary>
    internal Image Hairline(Transform parent, float alpha)
    {
        var line = Rect(parent, "Hairline").gameObject.AddComponent<Image>();
        line.raycastTarget = false;
        _rules.Add((line, alpha));
        ShowRule(line, alpha);
        return line;
    }

    private static void ShowRule(Image line, float alpha)
    {
        var sprite = DesktopNativeSkin.KitArt(DesktopKitArt.Divider);
        line.sprite = sprite;
        line.color = InGameTheme.WithAlpha(sprite != null ? Color.white : InGameTheme.Rule, alpha);
        var height = sprite != null ? RuleHeight : 1;
        // A rule outside layout is anchored to one edge, so its size delta is its height.
        if (Size(line, -1, height).ignoreLayout)
            line.rectTransform.sizeDelta = new Vector2(line.rectTransform.sizeDelta.x, height);
    }

    /// <summary>
    /// An icon in <paramref name="color"/>: the game's own glyph where it has one (close, search,
    /// the dropdown arrow) or BPP has one (the supporter heart), drawn from strokes otherwise and
    /// while the glyph is missing. Search and the heart have no strokes: without the glyph
    /// nothing shows.
    /// </summary>
    internal RectTransform Icon(Transform parent, KitIcon icon, float size, Color color)
    {
        var box = Rect(parent, $"Icon{icon}");
        Size(box, size, size);
        var strokes = Fill(Rect(box, "Strokes"));
        void Stroke(float length, Vector2 pivot, Vector2 at, float angle)
        {
            var bar = Rect(strokes, "Stroke");
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
        DesktopKitArt? art = null;
        var turn = 0f;
        switch (icon)
        {
            case KitIcon.Close:
                art = DesktopKitArt.IconClose;
                Stroke(size * 1.15f, half, Vector2.zero, 45);
                Stroke(size * 1.15f, half, Vector2.zero, -45);
                break;
            case KitIcon.Search:
                art = DesktopKitArt.IconSearch;
                break;
            case KitIcon.Heart:
                art = DesktopKitArt.IconHeart;
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
                // The game's arrow points down; the strokes draw a chevron pointing right.
                art = DesktopKitArt.IconArrow;
                turn = icon switch
                {
                    KitIcon.ChevronLeft => -90f,
                    KitIcon.ChevronRight => 90f,
                    _ => 0f,
                };
                strokes.localRotation = Quaternion.Euler(0, 0, turn - 90f);
                var tip = new Vector2(size * .2f, 0);
                Stroke(size * .55f, new Vector2(1, .5f), tip, -45);
                Stroke(size * .55f, new Vector2(1, .5f), tip, 45);
                break;
        }
        if (art is { } native)
        {
            var glyph = Fill(Rect(box, "Glyph")).gameObject.AddComponent<Image>();
            glyph.rectTransform.localRotation = Quaternion.Euler(0, 0, turn);
            glyph.preserveAspect = true;
            glyph.raycastTarget = false;
            glyph.color = color;
            _glyphs.Add((glyph, native));
            ShowGlyph(glyph, native);
        }
        return box;
    }

    // The glyph replaces its strokes once the game's sprite is in.
    private static void ShowGlyph(Image glyph, DesktopKitArt art)
    {
        var sprite = DesktopNativeSkin.KitArt(art);
        glyph.sprite = sprite;
        glyph.enabled = sprite != null;
        glyph.transform.parent.Find("Strokes")?.gameObject.SetActive(sprite == null);
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
        var glyph = Icon(row, icon, IconSize(icon, size), InGameTheme.Ink);
        button.Bind(kind, plate, glyph.GetComponentsInChildren<Graphic>(true));
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
        if (before is { } first)
            ink.AddRange(
                Icon(row, first, IconSize(first, size), InGameTheme.Ink)
                    .GetComponentsInChildren<Graphic>(true)
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
                Icon(row, last, IconSize(last, size), InGameTheme.Ink)
                    .GetComponentsInChildren<Graphic>(true)
            );
        button.Bind(kind, plate, ink.ToArray());
        return button;
    }

    // The game's arrow glyph has wide empty margins, so an arrow's box is larger than another
    // icon's for the same ink.
    private static float IconSize(KitIcon icon, KitSize size) =>
        icon is KitIcon.ChevronLeft or KitIcon.ChevronRight ? (size == KitSize.Md ? 30 : 24)
        : size == KitSize.Md ? 16
        : 14;

    /// <summary>
    /// A paging arrow: a plated md square with a large arrow, for a list's pager or a strip's
    /// ends. With <paramref name="wide"/> it shares its row's width and stands higher, for a
    /// panel whose main action is paging.
    /// </summary>
    internal KitButton PageArrow(Transform parent, bool forward, Action onClick, bool wide = false)
    {
        var button = IconButton(
            parent,
            forward ? KitIcon.ChevronRight : KitIcon.ChevronLeft,
            KitButtonKind.Secondary,
            KitSize.Md,
            onClick
        );
        if (wide)
        {
            var element = Size(button, -1, WideArrowHeight);
            element.minWidth = element.preferredWidth = Height(KitSize.Md);
            element.flexibleWidth = 1;
        }
        return button;
    }

    private (KitButton Button, RectTransform Row, KitPlate Plate) ButtonShell(
        Transform parent,
        KitSize size,
        Action onClick,
        string name
    )
    {
        var rect = Rect(parent, name);
        var button = rect.gameObject.AddComponent<KitButton>();
        var plate = Plate(rect, KitLook.None);
        plate.Image.raycastTarget = true;
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
    /// A segmented control: one darkened track, and only the selected option gets a plate. The track is a control height and its options sit inside it.
    /// </summary>
    internal (RectTransform Track, KitButton[] Options) Segmented(
        Transform parent,
        IReadOnlyList<string> labels,
        KitSize size,
        Action<int> onSelect
    )
    {
        var track = Rect(parent, "Segmented");
        Surface(track, KitSurface.Track);
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
        var plate = Plate(box, KitLook.None);
        plate.Image.raycastTarget = true;
        var mark = Icon(box, KitIcon.Check, boxSize * .6f, InGameTheme.AccentBright);
        mark.anchorMin = mark.anchorMax = new Vector2(.5f, .5f);
        mark.sizeDelta = new Vector2(boxSize * .6f, boxSize * .6f);
        mark.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        Text(rect, label, KitTextRole.Body);
        var button = rect.gameObject.AddComponent<KitButton>();
        button.onClick.AddListener(() => onClick());
        button.Bind(
            KitButtonKind.Check,
            plate,
            mark.GetComponentsInChildren<Graphic>(true),
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
        Icon(row, KitIcon.ChevronDown, 16, InGameTheme.InkMuted);
        // A field is sunk: the darkened centre at rest, the row hover wash on top of it.
        Surface(button.transform, KitSurface.Field);
        button.Bind(KitButtonKind.Row, plate, Array.Empty<Graphic>());
        return button;
    }

    /// <summary>A sunk single-line text field with the search glass and a placeholder.</summary>
    internal TMP_InputField Search(Transform parent, string placeholder) =>
        Field(parent, placeholder, KitIcon.Search);

    /// <summary>
    /// A sunk text field with a placeholder. With more than one line in
    /// <paramref name="lines"/> it is that many lines high and takes line breaks.
    /// </summary>
    internal TMP_InputField Field(
        Transform parent,
        string placeholder,
        KitIcon? icon = null,
        int lines = 1
    )
    {
        var rect = Rect(parent, "Field");
        var many = lines > 1;
        Size(
            rect,
            -1,
            many
                ? lines * (InGameSizes.TextBody + InGameSizes.Space6) + InGameSizes.Space16
                : InGameSizes.ControlMd
        );
        var ground = Surface(rect, KitSurface.Field).Image;
        ground.raycastTarget = true;
        var left = InGameSizes.Space12;
        if (icon is { } shown)
        {
            var glyph = Icon(rect, shown, InGameSizes.Space16, InGameTheme.InkMuted);
            glyph.anchorMin = glyph.anchorMax = new Vector2(0, .5f);
            glyph.pivot = new Vector2(0, .5f);
            glyph.anchoredPosition = new Vector2(InGameSizes.Space12, 0);
            glyph.sizeDelta = new Vector2(InGameSizes.Space16, InGameSizes.Space16);
            left = InGameSizes.Space12 * 2 + InGameSizes.Space16 - InGameSizes.Space4;
        }
        var viewport = Fill(Rect(rect, "Viewport"));
        var edge = many ? InGameSizes.Space8 : 0;
        viewport.offsetMin = new Vector2(left, edge);
        viewport.offsetMax = new Vector2(-InGameSizes.Space12, -edge);
        viewport.gameObject.AddComponent<RectMask2D>();
        var alignment = many ? TextAlignmentOptions.TopLeft : TextAlignmentOptions.MidlineLeft;
        var text = Text(viewport, "", KitTextRole.Body, alignment: alignment);
        Fill(text.rectTransform);
        var hint = Text(viewport, placeholder, KitTextRole.Body, InGameTheme.InkMuted, alignment);
        Fill(hint.rectTransform);
        if (many)
        {
            text.textWrappingMode = hint.textWrappingMode = TextWrappingModes.Normal;
            text.overflowMode = TextOverflowModes.Overflow;
        }
        var input = rect.gameObject.AddComponent<TMP_InputField>();
        input.transition = Selectable.Transition.None;
        input.targetGraphic = ground;
        input.textViewport = viewport;
        input.textComponent = text;
        input.placeholder = hint;
        input.lineType = many
            ? TMP_InputField.LineType.MultiLineNewline
            : TMP_InputField.LineType.SingleLine;
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
        Hairline(block, .7f);
        return block;
    }

    /// <summary>
    /// A selectable list row with no plate at rest; the consumer fills <paramref name="content"/>,
    /// a left-to-right row as wide as the column. The plate bleeds out of the row on each side,
    /// so the column needs that much room around it. <see cref="Dividers"/> draws the hairlines.
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
        var plate = Plate(rect, KitLook.None);
        plate.Image.raycastTarget = true;
        plate.Image.rectTransform.offsetMin = new Vector2(-RowBleed, 0);
        plate.Image.rectTransform.offsetMax = new Vector2(RowBleed, 0);
        var divider = Hairline(rect, .5f).rectTransform;
        divider.name = "Divider";
        divider.GetComponent<LayoutElement>().ignoreLayout = true;
        divider.anchorMin = new Vector2(0, 1);
        divider.anchorMax = new Vector2(1, 1);
        divider.pivot = new Vector2(.5f, 1);
        divider.offsetMin = new Vector2(0, -1);
        divider.offsetMax = Vector2.zero;
        ShowRule(divider.GetComponent<Image>(), .5f);
        content = Fill(Rect(rect, "Content"));
        var group = content.gameObject.AddComponent<HorizontalLayoutGroup>();
        group.padding = new RectOffset(0, 0, (int)InGameSizes.Space8, (int)InGameSizes.Space8);
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
    /// <summary>The same, giving the two texts to a consumer that changes them later.</summary>
    internal RectTransform TitleAndMeta(
        Transform parent,
        string title,
        string meta,
        out TextMeshProUGUI titleText,
        out TextMeshProUGUI metaText
    )
    {
        var column = Rect(parent, "TitleAndMeta");
        var group = column.gameObject.AddComponent<VerticalLayoutGroup>();
        group.spacing = InGameSizes.Space2;
        group.childAlignment = TextAnchor.MiddleLeft;
        group.childControlWidth = group.childControlHeight = true;
        group.childForceExpandWidth = true;
        group.childForceExpandHeight = false;
        titleText = Text(column, title, KitTextRole.Body);
        metaText = Text(column, meta, KitTextRole.Meta);
        metaText.richText = true;
        column.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
        return column;
    }

    /// <summary>An image of native art, such as a trophy or a badge, at a fixed size.</summary>
    internal static Image Art(Transform parent, Sprite? sprite, float width, float height)
    {
        var image = Rect(parent, "Art").gameObject.AddComponent<Image>();
        image.preserveAspect = true;
        image.raycastTarget = false;
        Size(image, width, height);
        Show(image, sprite);
        return image;
    }

    /// <summary>
    /// Puts a sprite on an art image; without one the image is clear. The image shows the
    /// sprite at once and changes to its small copy (<see cref="KitSmallArt"/>) when that is
    /// ready: the game's trophies and badges have no mipmaps and shimmer at this size.
    /// </summary>
    internal static void Show(Image image, Sprite? sprite)
    {
        if (image == null)
            return;
        Wanted.GetOrCreateValue(image).Value = sprite;
        image.sprite = sprite;
        image.color = sprite != null ? Color.white : Color.clear;
        if (sprite != null)
            ShowCopy(image, sprite, KitSmallArt.IconAsync(sprite), showSourceFirst: true);
    }

    private static async void ShowCopy(
        Image image,
        Sprite source,
        Task<Sprite?> copy,
        bool showSourceFirst
    )
    {
        var small = await copy;
        if (image == null || !Wanted.TryGetValue(image, out var wanted) || wanted.Value != source)
            return;
        if (small == null && showSourceFirst)
            return;
        image.sprite = small != null ? small : source;
        image.color = Color.white;
    }

    /// <summary>The sprite of a state mark, or null if its art cannot be decoded.</summary>
    internal static Sprite? Mark(KitMark mark) =>
        DesktopNativeSkin.KitArt(
            mark switch
            {
                KitMark.Abandoned => DesktopKitArt.MarkAbandoned,
                KitMark.Active => DesktopKitArt.MarkActive,
                KitMark.Won => DesktopKitArt.MarkWon,
                KitMark.Lost => DesktopKitArt.MarkLost,
                _ => DesktopKitArt.MarkCompleted,
            }
        );

    /// <summary>
    /// A hero portrait in a square well: a sunk field that a hero's medallion covers. With
    /// <paramref name="rise"/> the head stands above the well's top edge, so the well needs
    /// that room above it (a run row has it; a day cell under its caption has not).
    /// Returns the portrait image, for <see cref="ShowPortrait"/>.
    /// </summary>
    internal Image Portrait(Transform parent, Sprite? sprite, float size, bool rise = false)
    {
        var well = Rect(parent, "Portrait");
        Size(well, size, size);
        Surface(well, KitSurface.Field);
        // The medallion stands on the well's bottom edge; one that rises is larger than the
        // well by the rise, and its head stands above the well's top edge.
        var art = Rect(well, "Art");
        art.anchorMin = art.anchorMax = art.pivot = new Vector2(.5f, 0f);
        var side = size * (1f + (rise ? KitArtReduction.MedallionRise : 0f));
        art.sizeDelta = new Vector2(side, side);
        var image = art.gameObject.AddComponent<Image>();
        image.preserveAspect = true;
        image.raycastTarget = false;
        if (rise)
            Risen.Add(image, image);
        ShowPortrait(image, sprite);
        return image;
    }

    /// <summary>
    /// Puts a hero in a portrait well as a medallion (<c>KitArtReduction.Medallion</c>): the
    /// hero's colour <paramref name="glow"/> behind the head and shoulders, inside a thin rim.
    /// <paramref name="focus"/> names the face square (<c>HeroPortraitFocus</c> has one per
    /// hero); without it the kit finds the head. The well stays a sunk field without a sprite
    /// and while the medallion is drawn, and shows the whole portrait if it cannot be drawn.
    /// </summary>
    internal static void ShowPortrait(
        Image portrait,
        Sprite? sprite,
        KitPortraitFocus? focus = null,
        Color? glow = null
    )
    {
        if (portrait == null)
            return;
        Wanted.GetOrCreateValue(portrait).Value = sprite;
        portrait.sprite = null;
        portrait.color = Color.clear;
        if (sprite == null)
            return;
        var look = new KitMedallion(
            focus,
            Rgb(glow ?? InGameTheme.HeroNone),
            Rgb(InGameTheme.Backdrop),
            Rgb(InGameTheme.Accent),
            Risen.TryGetValue(portrait, out _)
        );
        ShowCopy(
            portrait,
            sprite,
            KitSmallArt.MedallionAsync(sprite, look),
            showSourceFirst: false
        );
    }

    private static KitRgb Rgb(Color32 color) => new(color.r, color.g, color.b);

    /// <summary>
    /// A run in a list: the hero's portrait well, a title over a meta line (which may hold rich
    /// text), then two fixed slots at the right: the rank badge and the mark (a trophy or a
    /// state mark), one size, side by side. So in every row the marks are on one axis at the
    /// row's right edge, whether or not the row has a rank; an unranked row keeps the empty
    /// rank slot. The row says how the run ended with the mark alone, not with a text.
    /// </summary>
    internal KitRunRow RunRow(
        Transform parent,
        float height,
        string title,
        string meta,
        Action onClick
    )
    {
        var button = ListRow(parent, height, onClick, out var content);
        var portrait = Portrait(content, null, RowPortrait, rise: true);
        TitleAndMeta(content, title, meta, out var titleText, out var metaText);
        var rank = Art(content, null, RowArt, RowArt);
        var mark = Art(content, null, RowArt, RowArt);
        return new KitRunRow(button, titleText, metaText, portrait, rank, mark);
    }

    /// <summary>
    /// A scrolling list for rows: its mask is wider than the list by the row plate's bleed, so
    /// a hover or selected plate keeps its round corners. Rows go in
    /// <paramref name="content"/>, top to bottom.
    /// </summary>
    internal ScrollRect ScrollList(Transform parent, out RectTransform content)
    {
        var rect = Rect(parent, "List");
        var scroll = rect.gameObject.AddComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = ScrollSensitivity;
        var viewport = Fill(Rect(rect, "Viewport"));
        viewport.offsetMin = new Vector2(-RowBleed, 0);
        viewport.offsetMax = new Vector2(RowBleed, 0);
        viewport.gameObject.AddComponent<RectMask2D>();
        // A mask takes pointer events only through a graphic.
        var catcher = viewport.gameObject.AddComponent<Image>();
        catcher.color = Color.clear;
        content = Rect(viewport, "Content");
        content.anchorMin = new Vector2(0, 1);
        content.anchorMax = Vector2.one;
        content.pivot = new Vector2(.5f, 1);
        content.offsetMin = content.offsetMax = Vector2.zero;
        var group = content.gameObject.AddComponent<VerticalLayoutGroup>();
        group.padding = new RectOffset((int)RowBleed, (int)RowBleed, 0, 0);
        group.childControlWidth = group.childControlHeight = true;
        group.childForceExpandWidth = true;
        group.childForceExpandHeight = false;
        content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter
            .FitMode
            .PreferredSize;
        scroll.viewport = viewport;
        scroll.content = content;
        return scroll;
    }

    /// <summary>
    /// The day strip: one darkened track holding a run's battles from the left, each one
    /// width; when they do not fit, the strip scrolls sideways. Add days to
    /// <see cref="KitDayStrip.Days"/> with <see cref="Day"/>. With
    /// <paramref name="newestFirst"/> the consumer adds the newest day first and the strip
    /// still shows the oldest at the left.
    /// </summary>
    internal KitDayStrip DayStrip(Transform parent, bool newestFirst = false)
    {
        var track = Rect(parent, "DayStrip");
        Size(track, -1, DayStripHeight);
        Surface(track, KitSurface.Track);
        var scroll = track.gameObject.AddComponent<ScrollRect>();
        scroll.vertical = false;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = ScrollSensitivity;
        // The track is the strip's surface, so the mask is inside it: a day at either end keeps
        // its plate's corners.
        var viewport = Fill(Rect(track, "Viewport"), InGameSizes.Space4);
        viewport.gameObject.AddComponent<RectMask2D>();
        var catcher = viewport.gameObject.AddComponent<Image>();
        catcher.color = Color.clear;
        var days = Rect(viewport, "Days");
        days.anchorMin = Vector2.zero;
        days.anchorMax = Vector2.one;
        days.pivot = new Vector2(0, .5f);
        days.offsetMin = days.offsetMax = Vector2.zero;
        var group = days.gameObject.AddComponent<HorizontalLayoutGroup>();
        group.spacing = InGameSizes.Space4;
        group.childAlignment = TextAnchor.MiddleLeft;
        group.reverseArrangement = newestFirst;
        group.childControlWidth = group.childControlHeight = true;
        group.childForceExpandWidth = false;
        group.childForceExpandHeight = true;
        days.gameObject.AddComponent<KitStripWidth>();
        scroll.viewport = viewport;
        scroll.content = days;
        return new KitDayStrip(track, days, scroll);
    }

    /// <summary>
    /// One battle in the day strip: the day, the opponent's portrait well and the outcome mark.
    /// </summary>
    internal KitDay Day(Transform days, string day, KitMark outcome, Action onClick)
    {
        var rect = Rect(days, "Day");
        Size(rect, DayWidth, -1);
        var button = rect.gameObject.AddComponent<KitButton>();
        var plate = Plate(rect, KitLook.None);
        plate.Image.raycastTarget = true;
        var column = rect.gameObject.AddComponent<VerticalLayoutGroup>();
        column.padding = new RectOffset(0, 0, (int)InGameSizes.Space6, (int)InGameSizes.Space6);
        column.spacing = InGameSizes.Space4;
        column.childAlignment = TextAnchor.MiddleCenter;
        column.childControlWidth = column.childControlHeight = true;
        column.childForceExpandWidth = column.childForceExpandHeight = false;
        var dayText = Text(rect, day, KitTextRole.Caption, alignment: TextAlignmentOptions.Center);
        // A cell is narrow while the strip finds its width, and an ellipsis that cuts the
        // first glyph clears the whole text (docs/architecture/text.md).
        dayText.overflowMode = TextOverflowModes.Overflow;
        var portrait = Portrait(rect, null, DayPortrait);
        var mark = Art(rect, Mark(outcome), DayMark, DayMark);
        button.onClick.AddListener(() => onClick());
        button.Bind(KitButtonKind.Day, plate, new Graphic[] { dayText });
        return new KitDay(button, dayText, portrait, mark);
    }

    /// <summary>
    /// A floating surface (a tooltip or a dialog card): the ground inside the soft gold
    /// line, so it keeps an edge over the scrim; without the ground sprite, a plain rectangle
    /// in <c>ingame-dialog</c>. Its children go in the
    /// returned rect, laid out top to bottom with <paramref name="padding"/>.
    /// </summary>
    internal RectTransform Floating(Transform parent, string name, float padding)
    {
        var rect = Rect(parent, name);
        Surface(rect, KitSurface.Floating);
        var group = rect.gameObject.AddComponent<VerticalLayoutGroup>();
        var pad = (int)padding;
        group.padding = new RectOffset(pad, pad, pad, pad);
        group.spacing = InGameSizes.Space8;
        group.childControlWidth = group.childControlHeight = true;
        group.childForceExpandWidth = true;
        group.childForceExpandHeight = false;
        return rect;
    }

    /// <summary>
    /// A modal layer over the whole panel: a scrim that takes every press under it, and a
    /// floating card in front of it. The layer has its own canvas in front of the panel's
    /// boards (<see cref="BppOverlaySorting.PanelForeground"/>) and starts hidden; the consumer
    /// shows <see cref="KitModal.Root"/>. The card is <paramref name="width"/> wide and as high
    /// as its children; a press on the scrim calls <paramref name="onScrim"/>.
    /// </summary>
    internal KitModal Modal(
        Transform parent,
        string name,
        float width,
        Color scrim,
        Action? onScrim = null
    )
    {
        var root = Fill(Rect(parent, name));
        root.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        var canvas = root.gameObject.AddComponent<Canvas>();
        canvas.overrideSorting = true;
        canvas.sortingOrder = BppOverlaySorting.PanelForeground;
        root.gameObject.AddComponent<GraphicRaycaster>();
        var shade = root.gameObject.AddComponent<Image>();
        shade.color = scrim;
        if (onScrim != null)
        {
            var dismiss = root.gameObject.AddComponent<Button>();
            dismiss.transition = Selectable.Transition.None;
            dismiss.targetGraphic = shade;
            dismiss.onClick.AddListener(() => onScrim());
        }
        var card = Floating(root, "Card", InGameSizes.Space24);
        card.anchorMin = card.anchorMax = card.pivot = new Vector2(.5f, .5f);
        card.sizeDelta = new Vector2(width, 0);
        card.GetComponent<VerticalLayoutGroup>().spacing = InGameSizes.Space12;
        card.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter
            .FitMode
            .PreferredSize;
        // The card takes the presses on it, so they do not reach the scrim.
        card.GetChild(0).GetComponent<Image>().raycastTarget = true;
        root.gameObject.SetActive(false);
        return new KitModal(root.gameObject, card);
    }
}
