#nullable enable
using BazaarPlusPlus.GameInterop.NativeSkin;
using BazaarPlusPlus.Infrastructure.UiTokens;
using UnityEngine;
using UnityEngine.UI;

namespace BazaarPlusPlus.Infrastructure.DesktopKit;

internal enum KitButtonKind
{
    Primary,

    Secondary,

    Quiet,

    Danger,

    DangerConfirm,

    Segment,

    Chip,

    Check,

    Row,

    Day,
}

internal enum KitPreviewState
{
    Live,
    Hover,
    Pressed,
}

internal readonly struct KitLook
{
    internal readonly DesktopKitArt Art;
    internal readonly Color Tint;
    internal readonly Color Flat;
    internal readonly DesktopKitArt? Edge;
    internal readonly Color EdgeTint;

    internal KitLook(
        DesktopKitArt art,
        Color tint,
        Color flat,
        DesktopKitArt? edge = null,
        Color edgeTint = default
    )
    {
        Art = art;
        Tint = tint;
        Flat = flat;
        Edge = edge;
        EdgeTint = edgeTint;
    }

    internal static KitLook None => Wash(Color.clear);

    internal static KitLook Wash(Color color) => new(DesktopKitArt.Shape, color, color);

    internal KitLook Times(Color tint) => new(Art, Tint * tint, Flat * tint, Edge, EdgeTint * tint);
}

internal sealed class KitPlate
{
    // uGUI's default canvas reference, which every kit canvas keeps.
    private const float ReferencePixelsPerUnit = 100f;

    private Image? _edge;
    private KitLook _look;

    internal KitPlate(Image image, KitLook look)
    {
        Image = image;
        _look = look;
        Refresh();
    }

    internal Image Image { get; }

    internal void Show(KitLook look)
    {
        _look = look;
        Refresh();
    }

    internal void Refresh()
    {
        if (Image == null)
            return;
        var art = DesktopNativeSkin.KitArt(_look.Art);
        var drawn = art != null ? _look.Art : DesktopKitArt.Shape;
        Draw(Image, drawn, art != null ? _look.Tint : _look.Flat);
        var frame = art != null && _look.Edge is { } edge ? DesktopNativeSkin.KitArt(edge) : null;
        if (frame == null)
        {
            if (_edge != null)
                _edge.gameObject.SetActive(false);
            return;
        }
        if (_edge == null)
        {
            _edge = DesktopKit
                .Fill(DesktopKit.Rect(Image.transform, "Edge"))
                .gameObject.AddComponent<Image>();
            _edge.raycastTarget = false;
        }
        _edge.gameObject.SetActive(true);
        Draw(_edge, _look.Edge!.Value, _look.EdgeTint);
    }

    // The slice scale keeps corners and edge lines the same size on every plate.
    internal static void Draw(Image image, DesktopKitArt art, Color color)
    {
        var sprite = DesktopNativeSkin.KitArt(art);
        image.sprite = sprite;
        image.color = color;
        if (sprite == null)
            return;
        image.type = sprite.border != Vector4.zero ? Image.Type.Sliced : Image.Type.Simple;
        // An owned plate has its own scale; a game sprite draws so that its own height is one
        // md control.
        image.pixelsPerUnitMultiplier = art switch
        {
            <= DesktopKitArt.Ground => DesktopNativeSkin.KitPixelsPerUnit,
            _ => sprite.rect.height
                / sprite.pixelsPerUnit
                * ReferencePixelsPerUnit
                / InGameSizes.ControlMd,
        };
    }
}

// Unity leaves a clicked button Selected. The kit uses its own On state for selections.
internal sealed class KitButton : Button
{
    private KitPlate? _plate;
    private Graphic[] _ink = System.Array.Empty<Graphic>();
    private GameObject? _mark;
    private KitButtonKind _kind;
    private bool _on;
    private bool _dimmed;
    private Color? _inkOverride;
    private SelectionState _state;
    private KitPreviewState _preview;
    private TextMeshProUGUI? _label;

    internal KitButtonKind Kind => _kind;

    internal bool On
    {
        get => _on;
        set
        {
            if (_on == value)
                return;
            _on = value;
            Paint();
        }
    }

    /// <summary>Shown as unavailable but still pressable, so a press can say why.</summary>
    internal bool Dimmed
    {
        get => _dimmed;
        set
        {
            if (_dimmed == value)
                return;
            _dimmed = value;
            Paint();
        }
    }

    internal Color? InkOverride
    {
        get => _inkOverride;
        set
        {
            _inkOverride = value;
            Paint();
        }
    }

    internal string Label
    {
        set
        {
            _label ??= GetComponentInChildren<TextMeshProUGUI>(true);
            if (_label != null && _label.text != value)
                _label.text = value;
        }
    }

    internal KitPreviewState Preview
    {
        get => _preview;
        set
        {
            _preview = value;
            Paint();
        }
    }

    internal void Bind(KitButtonKind kind, KitPlate plate, Graphic[] ink, GameObject? mark = null)
    {
        _kind = kind;
        _plate = plate;
        _ink = ink;
        _mark = mark;
        transition = Transition.None;
        targetGraphic = plate.Image;
        Paint();
    }

    public override void DoStateTransition(SelectionState state, bool instant)
    {
        _state = state;
        Paint();
    }

    internal void Paint()
    {
        if (_plate == null)
            return;
        var disabled = _state == SelectionState.Disabled;
        var pressed = _state == SelectionState.Pressed || _preview == KitPreviewState.Pressed;
        // Unity keeps a clicked button Selected; under the pointer that still reads as hover.
        var hover =
            _state == SelectionState.Highlighted
            || (_state == SelectionState.Selected && isPointerInside)
            || _preview == KitPreviewState.Hover;
        var (look, ink) = KitPalette.Resolve(_kind, _on, _dimmed, disabled, hover, pressed);
        if (_inkOverride is { } custom && !_on && !disabled)
            ink = custom;
        _plate.Show(look);
        foreach (var graphic in _ink)
        {
            if (graphic != null)
                graphic.color = ink;
        }
        if (_mark != null)
            _mark.SetActive(_on);
    }
}

internal static class KitPalette
{
    private static readonly Color White = Color.white;

    internal static KitLook Brown(Color flat) => new(DesktopKitArt.Plate, White, flat);

    internal static KitLook Selected =>
        new(DesktopKitArt.Plate, White, InGameTheme.ButtonSelected, DesktopKitArt.Edge, White);

    internal static (KitLook Plate, Color Ink) Resolve(
        KitButtonKind kind,
        bool on,
        bool dimmed,
        bool disabled,
        bool hover,
        bool pressed
    )
    {
        var (rest, hoverLook, pressedLook, selected, ink, selectedInk, hoverInk) = Looks(kind);
        // Keeping the disabled plate preserves the row's shape.
        if (disabled)
            return (rest.Times(InGameTheme.TintPressed), Faded(ink));
        if (dimmed)
            return (
                rest.Times(hover ? InGameTheme.TintDimmedHover : InGameTheme.TintDisabled),
                InGameTheme.InkMuted
            );
        if (on)
            return (selected, selectedInk);
        if (pressed)
            return (pressedLook, hoverInk);
        if (hover)
            return (hoverLook, hoverInk);
        return (rest, ink);
    }

    private static (
        KitLook RestLook,
        KitLook Hover,
        KitLook Pressed,
        KitLook Selected,
        Color Ink,
        Color SelectedInk,
        Color HoverInk
    ) Looks(KitButtonKind kind)
    {
        var brown = Brown(InGameTheme.Button);
        var brownHover = new KitLook(DesktopKitArt.PlateHover, White, InGameTheme.Chip);
        var brownPressed = new KitLook(
            DesktopKitArt.PlatePressed,
            White,
            InGameTheme.Chip * InGameTheme.TintPressed
        );
        var wash = KitLook.Wash(InGameTheme.PlateHover);
        var washPressed = KitLook.Wash(InGameTheme.PlateHover * InGameTheme.TintPressed);
        return kind switch
        {
            KitButtonKind.Primary => (
                new KitLook(DesktopKitArt.PrimaryPlate, White, InGameTheme.ChipActive),
                new KitLook(DesktopKitArt.PrimaryPlateHover, White, InGameTheme.Accent),
                new KitLook(
                    DesktopKitArt.PrimaryPlate,
                    InGameTheme.TintPressed,
                    InGameTheme.ChipActive * InGameTheme.TintPressed
                ),
                Selected,
                InGameTheme.InkBright,
                InGameTheme.InkBright,
                InGameTheme.InkBright
            ),
            KitButtonKind.Secondary => (
                brown,
                brownHover,
                brownPressed,
                Selected,
                InGameTheme.Ink,
                InGameTheme.Accent,
                InGameTheme.InkBright
            ),
            KitButtonKind.Danger => (
                brown,
                brownHover,
                brownPressed,
                Selected,
                InGameTheme.Failure,
                InGameTheme.Failure,
                // The failure red does not read on the lit centre.
                InGameTheme.InkBright
            ),
            KitButtonKind.DangerConfirm => (
                new KitLook(DesktopKitArt.DangerPlate, White, InGameTheme.Failure),
                new KitLook(
                    DesktopKitArt.DangerPlateHover,
                    White,
                    Color.Lerp(InGameTheme.Failure, InGameTheme.InkBright, .18f)
                ),
                new KitLook(
                    DesktopKitArt.DangerPlatePressed,
                    White,
                    InGameTheme.Failure * InGameTheme.TintPressed
                ),
                new KitLook(DesktopKitArt.DangerPlate, White, InGameTheme.Failure),
                InGameTheme.InkBright,
                InGameTheme.InkBright,
                InGameTheme.InkBright
            ),
            KitButtonKind.Chip => (
                brown.Times(InGameTheme.TintRest),
                brown,
                brownPressed,
                new KitLook(DesktopKitArt.PlateHover, White, InGameTheme.ChipActive),
                InGameTheme.InkMuted,
                InGameTheme.InkBright,
                InGameTheme.Ink
            ),
            KitButtonKind.Check => (
                new KitLook(
                    DesktopKitArt.Plate,
                    White,
                    InGameTheme.Chip,
                    DesktopKitArt.Edge,
                    InGameTheme.ArtButtonTint
                ),
                new KitLook(
                    DesktopKitArt.Plate,
                    White,
                    InGameTheme.Chip,
                    DesktopKitArt.Edge,
                    White
                ),
                new KitLook(
                    DesktopKitArt.PlatePressed,
                    White,
                    InGameTheme.Chip,
                    DesktopKitArt.Edge,
                    White
                ),
                new KitLook(
                    DesktopKitArt.Plate,
                    White,
                    InGameTheme.ButtonSelected,
                    DesktopKitArt.EdgeBright,
                    White
                ),
                InGameTheme.AccentBright,
                InGameTheme.AccentBright,
                InGameTheme.AccentBright
            ),
            KitButtonKind.Row => (
                KitLook.None,
                wash,
                washPressed,
                Selected,
                InGameTheme.Ink,
                InGameTheme.Ink,
                InGameTheme.Ink
            ),
            _ => (
                KitLook.None,
                wash,
                washPressed,
                Selected,
                InGameTheme.InkMuted,
                InGameTheme.Accent,
                InGameTheme.Ink
            ),
        };
    }

    private static Color Faded(Color ink) => InGameTheme.WithAlpha(ink, ink.a * .45f);
}
