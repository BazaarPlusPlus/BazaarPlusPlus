#nullable enable
using BazaarPlusPlus.Infrastructure.UiTokens;
using UnityEngine;
using UnityEngine.UI;

namespace BazaarPlusPlus.Infrastructure.DesktopKit;

/// <summary>What a kit control looks like at rest; its states follow from it.</summary>
internal enum KitButtonKind
{
    /// <summary>The one solid action of a view: an ingame-accent plate.</summary>
    Primary,

    /// <summary>The default button: an ingame-button plate.</summary>
    Secondary,

    /// <summary>A text button with a plate only under the pointer.</summary>
    Quiet,

    /// <summary>A secondary button with ingame-failure text.</summary>
    Danger,

    /// <summary>A dialog's destructive confirm: an ingame-failure plate.</summary>
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
/// A uGUI button that paints its own plate and ink from the kit's states
/// (design/components/InGameKit/README.md). A state changes colours, never the sprite. "On" is
/// the kit's selected state (a selected segment, chip, row or toggle), not Unity's keyboard
/// selection, which a click leaves behind and the kit ignores.
/// </summary>
internal sealed class KitButton : Button
{
    private Image? _plate;
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

    internal void Bind(KitButtonKind kind, Image plate, Graphic[] ink, GameObject? mark = null)
    {
        _kind = kind;
        _plate = plate;
        _ink = ink;
        _mark = mark;
        transition = Transition.None;
        targetGraphic = plate;
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
        var (plate, ink) = KitPalette.Resolve(_kind, _on, _dimmed, disabled, hover, pressed);
        if (_inkOverride is { } custom && !_on && !disabled)
            ink = custom;
        _plate.color = plate;
        foreach (var graphic in _ink)
        {
            if (graphic != null)
                graphic.color = ink;
        }
        if (_mark != null)
            _mark.SetActive(_on);
    }
}

/// <summary>The kit's state colours, one place for every kind (InGameKit README, Components).</summary>
internal static class KitPalette
{
    internal static (Color Plate, Color Ink) Resolve(
        KitButtonKind kind,
        bool on,
        bool dimmed,
        bool disabled,
        bool hover,
        bool pressed
    )
    {
        var (rest, hoverPlate, selected, ink, selectedInk, hoverInk) = Colours(kind);
        if (disabled)
            return (rest * InGameTheme.TintDisabled, Faded(ink));
        if (dimmed)
            return (
                hover ? rest * InGameTheme.TintDimmedHover : rest * InGameTheme.TintDisabled,
                kind == KitButtonKind.Primary ? ink : InGameTheme.InkMuted
            );
        if (on)
            return (selected, selectedInk);
        if (pressed)
            return (hoverPlate * InGameTheme.TintPressed, hoverInk);
        if (hover)
            return (hoverPlate, hoverInk);
        return (rest, ink);
    }

    private static (
        Color RestPlate,
        Color Hover,
        Color Selected,
        Color Ink,
        Color SelectedInk,
        Color HoverInk
    ) Colours(KitButtonKind kind) =>
        kind switch
        {
            KitButtonKind.Primary => (
                InGameTheme.Accent,
                InGameTheme.AccentBright,
                InGameTheme.Accent,
                InGameTheme.Backdrop,
                InGameTheme.Backdrop,
                InGameTheme.Backdrop
            ),
            KitButtonKind.Secondary => (
                InGameTheme.Button,
                InGameTheme.Chip,
                InGameTheme.ButtonSelected,
                InGameTheme.Ink,
                InGameTheme.Accent,
                InGameTheme.Ink
            ),
            KitButtonKind.Danger => (
                InGameTheme.Button,
                InGameTheme.Chip,
                InGameTheme.ButtonSelected,
                InGameTheme.Failure,
                InGameTheme.Failure,
                InGameTheme.Failure
            ),
            KitButtonKind.DangerConfirm => (
                InGameTheme.Failure,
                Color.Lerp(InGameTheme.Failure, InGameTheme.InkBright, .18f),
                InGameTheme.Failure,
                InGameTheme.Backdrop,
                InGameTheme.Backdrop,
                InGameTheme.Backdrop
            ),
            KitButtonKind.Quiet => (
                Color.clear,
                InGameTheme.PlateHover,
                InGameTheme.ButtonSelected,
                InGameTheme.InkMuted,
                InGameTheme.Accent,
                InGameTheme.Ink
            ),
            KitButtonKind.Segment => (
                Color.clear,
                InGameTheme.PlateHover,
                InGameTheme.ButtonSelected,
                InGameTheme.InkMuted,
                InGameTheme.Accent,
                InGameTheme.Ink
            ),
            KitButtonKind.Chip => (
                InGameTheme.Chip,
                Color.Lerp(InGameTheme.Chip, InGameTheme.ChipActive, .35f),
                InGameTheme.ChipActive,
                InGameTheme.InkMuted,
                InGameTheme.Ink,
                InGameTheme.Ink
            ),
            KitButtonKind.Check => (
                InGameTheme.Chip,
                Color.Lerp(InGameTheme.Chip, InGameTheme.ChipActive, .35f),
                InGameTheme.Accent,
                InGameTheme.Backdrop,
                InGameTheme.Backdrop,
                InGameTheme.Backdrop
            ),
            KitButtonKind.Day => (
                Color.clear,
                InGameTheme.PlateHover,
                InGameTheme.ButtonSelected,
                InGameTheme.InkMuted,
                InGameTheme.Accent,
                InGameTheme.Ink
            ),
            _ => (
                Color.clear,
                InGameTheme.PlateHover,
                InGameTheme.ButtonSelected,
                InGameTheme.Ink,
                InGameTheme.Ink,
                InGameTheme.Ink
            ),
        };

    private static Color Faded(Color ink) => InGameTheme.WithAlpha(ink, ink.a * .5f);
}
