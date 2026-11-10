#nullable enable
using BazaarPlusPlus.GameInterop.NativeSkin;
using BazaarPlusPlus.Infrastructure.UiTokens;
using UnityEngine;
using UnityEngine.UIElements;

namespace BazaarPlusPlus.Infrastructure.DesktopKit;

/// <summary>
/// The Desktop UI kit's plates on a UI Toolkit element, for a panel whose content is a UI
/// Toolkit tree (Collection). The element's own fill, border and corner radius give way to
/// the plate, which carries them; without the plate's sprite the element keeps its flat style.
/// </summary>
internal static class KitToolkit
{
    /// <summary>Draws <paramref name="art"/> behind the element, sliced like a kit plate.</summary>
    internal static bool Paint(VisualElement element, DesktopKitArt art, Color? tint = null)
    {
        var sprite = DesktopNativeSkin.KitArt(art);
        if (sprite == null)
            return false;
        var style = element.style;
        style.backgroundImage = new StyleBackground(sprite);
        style.unityBackgroundImageTintColor = tint ?? Color.white;
        style.backgroundColor = Color.clear;
        var border = (int)DesktopNativeSkin.KitBorderPixels;
        style.unitySliceLeft = style.unitySliceRight = border;
        style.unitySliceTop = style.unitySliceBottom = border;
        style.unitySliceScale = 1f / DesktopNativeSkin.KitPixelsPerUnit;
        style.borderLeftWidth = style.borderRightWidth = 0f;
        style.borderTopWidth = style.borderBottomWidth = 0f;
        style.borderTopLeftRadius = style.borderTopRightRadius = 0f;
        style.borderBottomLeftRadius = style.borderBottomRightRadius = 0f;
        return true;
    }

    /// <summary>A button's plate at rest, under the pointer and pressed.</summary>
    internal static void PaintButton(VisualElement button)
    {
        if (!Paint(button, DesktopKitArt.Plate))
            return;
        var over = false;
        void Show(DesktopKitArt art) => Paint(button, art);
        button.RegisterCallback<MouseEnterEvent>(_ =>
        {
            over = true;
            Show(DesktopKitArt.PlateHover);
        });
        button.RegisterCallback<MouseLeaveEvent>(_ =>
        {
            over = false;
            Show(DesktopKitArt.Plate);
        });
        button.RegisterCallback<PointerDownEvent>(
            _ => Show(DesktopKitArt.PlatePressed),
            TrickleDown.TrickleDown
        );
        button.RegisterCallback<PointerUpEvent>(
            _ => Show(over ? DesktopKitArt.PlateHover : DesktopKitArt.Plate),
            TrickleDown.TrickleDown
        );
    }

    /// <summary>The kit's track: the plate darkened, for a strip that holds other controls.</summary>
    internal static bool PaintTrack(VisualElement element) =>
        Paint(element, DesktopKitArt.Plate, InGameTheme.TintPressed);

    /// <summary>An element showing one of the kit's glyphs in <paramref name="color"/>.</summary>
    internal static VisualElement Glyph(DesktopKitArt art, float size, Color color)
    {
        var glyph = new VisualElement { pickingMode = PickingMode.Ignore };
        glyph.style.width = glyph.style.height = size;
        glyph.style.flexShrink = 0f;
        if (DesktopNativeSkin.KitArt(art) is { } sprite)
        {
            glyph.style.backgroundImage = new StyleBackground(sprite);
            glyph.style.unityBackgroundImageTintColor = color;
        }
        return glyph;
    }
}
