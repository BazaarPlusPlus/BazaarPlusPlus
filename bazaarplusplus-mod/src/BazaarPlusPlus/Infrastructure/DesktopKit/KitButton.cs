#nullable enable
using BazaarPlusPlus.GameInterop.NativeSkin;
using BazaarPlusPlus.Infrastructure.UiTokens;
using UnityEngine;
using UnityEngine.UI;

namespace BazaarPlusPlus.Infrastructure.DesktopKit;

/// <summary>What a kit control looks like at rest; its states follow from it.</summary>
internal enum KitButtonKind
{
    /// <summary>The one leading action of a view: the game's lit brown centre in its bright frame.</summary>
    Primary,

    /// <summary>The default button: the game's brown centre.</summary>
    Secondary,

    /// <summary>A text button with a wash only under the pointer.</summary>
    Quiet,

    /// <summary>A secondary button with ingame-failure text.</summary>
    Danger,

    /// <summary>A dialog's destructive confirm: the game's red centre.</summary>
    DangerConfirm,

    /// <summary>One option of a segmented control.</summary>
    Segment,

    /// <summary>A filter chip.</summary>
    Chip,

    /// <summary>A checkbox's box.</summary>
    Check,

    /// <summary>A list row or a sunk field: no plate at rest.</summary>
    Row,

    /// <summary>A day of the day strip: no plate at rest, its day in ingame-accent when selected.</summary>
    Day,
}

/// <summary>A state the Kit Gallery holds a control in; Live follows the pointer.</summary>
internal enum KitPreviewState
{
    Live,
    Hover,
    Pressed,
}

/// <summary>
/// What a plate shows: a game sprite under a tint, an optional frame sprite over it, and the
/// token colour the plate takes on the solid shape while its sprite is missing.
/// </summary>
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

    /// <summary>No plate.</summary>
    internal static KitLook None => Wash(Color.clear);

    /// <summary>The solid shape in one colour: a hover wash.</summary>
    internal static KitLook Wash(Color color) => new(DesktopKitArt.Shape, color, color);

    /// <summary>The same look with its sprite, frame and fallback multiplied by a tint.</summary>
    internal KitLook Times(Color tint) => new(Art, Tint * tint, Flat * tint, Edge, EdgeTint * tint);
}

/// <summary>
/// A plate of the kit: an image that shows a <see cref="KitLook"/>, and its frame image, made
/// when a look first asks for one. <see cref="Refresh"/> draws the look again after a sprite
/// arrives.
/// </summary>
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

    // A sliced sprite at the scale where its own height is one md control, so a chamfer or a
    // frame line is the same size on every plate. Without the sprite, a plain rectangle.
    internal static void Draw(Image image, DesktopKitArt art, Color color)
    {
        var sprite = DesktopNativeSkin.KitArt(art);
        image.sprite = sprite;
        image.color = color;
        if (sprite == null)
            return;
        image.type = sprite.border != Vector4.zero ? Image.Type.Sliced : Image.Type.Simple;
        image.pixelsPerUnitMultiplier = art switch
        {
            DesktopKitArt.Shape => DesktopNativeSkin.ShapePixelsPerUnit,
            DesktopKitArt.Ground => DesktopNativeSkin.GroundPixelsPerUnit,
            _ => sprite.rect.height
                / sprite.pixelsPerUnit
                * ReferencePixelsPerUnit
                / InGameSizes.ControlMd,
        };
    }
}

/// <summary>
/// A uGUI button that paints its own plate and ink from the kit's states
/// (design/components/InGameKit/README.md). A state is the game's own sprite for it where the
/// game has one. "On" is the kit's selected state (a selected segment, chip, row or toggle), not
/// Unity's keyboard selection, which a click leaves behind and the kit ignores.
/// </summary>
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

    internal KitButtonKind Kind => _kind;

    /// <summary>The kit's selected state.</summary>
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

    /// <summary>A label colour that replaces the kind's ink at rest, such as a card tier.</summary>
    internal Color? InkOverride
    {
        get => _inkOverride;
        set
        {
            _inkOverride = value;
            Paint();
        }
    }

    /// <summary>Holds a state for the Kit Gallery, which shows every state side by side.</summary>
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

/// <summary>The kit's state looks, one place for every kind (InGameKit README, Components).</summary>
internal static class KitPalette
{
    private static readonly Color White = Color.white;

    /// <summary>The game's brown centre at rest, with the token it falls back to.</summary>
    internal static KitLook Brown(Color flat) => new(DesktopKitArt.Plate, White, flat);

    /// <summary>A selected control or row: the brown centre in the game's thin gold frame.</summary>
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
        if (disabled)
            return (rest.Times(InGameTheme.TintDisabled), Faded(ink));
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
        // Under the pointer a plate takes the game's lit centre, darkened so it stays below the
        // primary button, which shows that centre at full.
        var brownHover = new KitLook(
            DesktopKitArt.PlateHover,
            InGameTheme.TintPressed,
            InGameTheme.Chip
        );
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
                new KitLook(
                    DesktopKitArt.PlateHover,
                    White,
                    InGameTheme.ButtonSelected,
                    DesktopKitArt.EdgeBright,
                    White
                ),
                new KitLook(
                    DesktopKitArt.PlateHover,
                    DesktopNativeSkin.OverBrightHover,
                    InGameTheme.ChipActive,
                    DesktopKitArt.EdgeBright,
                    White
                ),
                new KitLook(
                    DesktopKitArt.PlatePressed,
                    White,
                    InGameTheme.ButtonSelected * InGameTheme.TintPressed,
                    DesktopKitArt.Edge,
                    White
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
                new KitLook(
                    DesktopKitArt.PlateHover,
                    InGameTheme.TintPressed,
                    InGameTheme.ChipActive
                ),
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
            // Quiet, Segment and Day: no plate at rest, a wash under the pointer.
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

    private static Color Faded(Color ink) => InGameTheme.WithAlpha(ink, ink.a * .5f);
}
