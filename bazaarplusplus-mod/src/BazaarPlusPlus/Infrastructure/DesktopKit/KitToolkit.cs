#nullable enable
using BazaarPlusPlus.GameInterop.NativeSkin;
using UnityEngine;
using UnityEngine.UIElements;

namespace BazaarPlusPlus.Infrastructure.DesktopKit;

/// <summary>The Desktop UI kit's art on a UI Toolkit element (Collection's supporter strip).</summary>
internal static class KitToolkit
{
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
