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
/// here is area-averaged once and has its own mipmaps; a portrait copy is drawn as the
/// hero's medallion. One copy per source sprite; a copy that cannot be made is null
/// and the kit shows the source.
/// </summary>
internal static class KitSmallArt
{
    private const int CapturePixels = 512;
    private const int IconPixels = 128;
    private const int PlatePixels = 176;

    private static readonly Dictionary<int, (Sprite Source, Task<Sprite?> Copy)> Icons = new();
    private static readonly Dictionary<int, (Sprite Source, Task<Sprite?> Copy)> Heads = new();
    private static readonly Dictionary<int, (Sprite Source, Task<Sprite?> Copy)> RisenHeads = new();

    /// <summary>The whole sprite, small.</summary>
    internal static Task<Sprite?> IconAsync(Sprite source) => Get(Icons, source, false, null);

    /// <summary>A hero's medallion, drawn from a bust portrait (<see cref="KitArtReduction.Medallion"/>).</summary>
    internal static Task<Sprite?> MedallionAsync(Sprite source, KitMedallion look) =>
        Get(look.Rise ? RisenHeads : Heads, source, true, look);

    private static Task<Sprite?> Get(
        Dictionary<int, (Sprite Source, Task<Sprite?> Copy)> copies,
        Sprite source,
        bool head,
        KitMedallion? look
    )
    {
        var id = source.GetInstanceID();
        if (copies.TryGetValue(id, out var entry) && entry.Source == source)
            return entry.Copy;
        var copy = MakeAsync(source, head, look);
        copies[id] = (source, copy);
        return copy;
    }

    private static async Task<Sprite?> MakeAsync(Sprite source, bool head, KitMedallion? look)
    {
        try
        {
            var name = source.name;
            var image = await NativePortraitTexture.CaptureAsync(
                source.texture,
                source.textureRect,
                CapturePixels
            );
            var small = await Task.Run(() =>
                head && look is { } medallion
                    ? KitArtReduction.Medallion(image, medallion, PlatePixels)
                    : Icon(image)
            );
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
