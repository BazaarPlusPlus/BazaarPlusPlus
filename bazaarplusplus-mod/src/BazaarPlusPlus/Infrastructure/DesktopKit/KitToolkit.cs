#nullable enable
using BazaarPlusPlus.GameInterop.NativeSkin;
using UnityEngine;
using UnityEngine.UIElements;

namespace BazaarPlusPlus.Infrastructure.DesktopKit;

internal static class KitToolkit
{
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
