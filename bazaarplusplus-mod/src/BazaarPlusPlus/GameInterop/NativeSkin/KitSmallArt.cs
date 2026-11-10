#nullable enable
using BazaarPlusPlus.GameInterop.HeroPortraits;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.DesktopKit;
using BazaarPlusPlus.Infrastructure.HeroPortraits.Composition;
using BazaarPlusPlus.Infrastructure.Logging;
using UnityEngine;

namespace BazaarPlusPlus.GameInterop.NativeSkin;

/// <summary>
/// Small copies of content art for the Desktop UI kit. The game's portraits, trophies and rank
/// badges are large textures without mipmaps, so a 30 or 44 unit copy of one shimmers. A copy
/// here is area-averaged once and has its own mipmaps; a portrait copy is also cut to the
/// hero's head and shoulders. One copy per source sprite; a copy that cannot be made is null
/// and the kit shows the source.
/// </summary>
internal static class KitSmallArt
{
    private const int CapturePixels = 512;
    private const int IconPixels = 128;
    private const int HeadPixels = 192;

    private static readonly Dictionary<int, (Sprite Source, Task<Sprite?> Copy)> Icons = new();
    private static readonly Dictionary<int, (Sprite Source, Task<Sprite?> Copy)> Heads = new();

    /// <summary>The whole sprite, small.</summary>
    internal static Task<Sprite?> IconAsync(Sprite source) => Get(Icons, source, false, null);

    /// <summary>The head and shoulders of a bust portrait, square.</summary>
    internal static Task<Sprite?> HeadAsync(Sprite source, KitPortraitFocus? focus) =>
        Get(Heads, source, true, focus);

    private static Task<Sprite?> Get(
        Dictionary<int, (Sprite Source, Task<Sprite?> Copy)> copies,
        Sprite source,
        bool head,
        KitPortraitFocus? focus
    )
    {
        var id = source.GetInstanceID();
        if (copies.TryGetValue(id, out var entry) && entry.Source == source)
            return entry.Copy;
        var copy = MakeAsync(source, head, focus);
        copies[id] = (source, copy);
        return copy;
    }

    private static async Task<Sprite?> MakeAsync(Sprite source, bool head, KitPortraitFocus? focus)
    {
        try
        {
            var name = source.name;
            var image = await NativePortraitTexture.CaptureAsync(
                source.texture,
                source.textureRect,
                CapturePixels
            );
            var small = await Task.Run(() => head ? Head(image, focus) : Icon(image));
            if (small == null || source == null)
                return null;
            var texture = new Texture2D(
                small.Width,
                small.Height,
                TextureFormat.RGBA32,
                mipChain: true
            )
            {
                name = $"BPP_KitSmallArt_{name}",
                filterMode = FilterMode.Trilinear,
                wrapMode = TextureWrapMode.Clamp,
                // Between two mip levels, lean to the sharper one: the art is small and still.
                mipMapBias = -.5f,
            };
            texture.SetPixelData(small.Rgba, 0);
            texture.Apply(updateMipmaps: true, makeNoLongerReadable: true);
            var sprite = Sprite.Create(
                texture,
                new Rect(0, 0, small.Width, small.Height),
                new Vector2(.5f, .5f),
                100f,
                extrude: 0u,
                SpriteMeshType.FullRect
            );
            sprite.name = texture.name;
            return sprite;
        }
        catch (Exception ex)
        {
            BppLog.WarnEvent(
                new BppLogEvent(
                    BppLogFeatureScope.Plugin,
                    "plugin.kit_small_art.failed",
                    storm: ["sprite"]
                ),
                ("sprite", source != null ? source.name : "destroyed"),
                ("error", ex.Message)
            );
            return null;
        }
    }

    private static PortraitImage? Head(PortraitImage image, KitPortraitFocus? focus)
    {
        if (KitArtReduction.HeadSquare(image, focus) is not { } square)
            return null;
        return KitArtReduction.Reduce(
            image,
            square.Left,
            square.Bottom,
            square.Side,
            square.Side,
            HeadPixels,
            HeadPixels
        );
    }

    private static PortraitImage Icon(PortraitImage image)
    {
        var factor = Math.Min(1f, (float)IconPixels / Math.Max(image.Width, image.Height));
        return KitArtReduction.Reduce(
            image,
            0,
            0,
            image.Width,
            image.Height,
            Math.Max(1, (int)MathF.Round(image.Width * factor)),
            Math.Max(1, (int)MathF.Round(image.Height * factor))
        );
    }
}
