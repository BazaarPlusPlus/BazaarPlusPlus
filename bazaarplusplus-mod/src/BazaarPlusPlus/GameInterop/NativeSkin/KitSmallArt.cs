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

    // One copy per source sprite and look: the same portrait may be asked for with two looks.
    private static readonly Dictionary<
        (int Sprite, KitMedallion? Look),
        (Sprite Source, Task<Sprite?> Copy)
    > Copies = new();

    /// <summary>The whole sprite, small.</summary>
    internal static Task<Sprite?> IconAsync(Sprite source) => Get(source, null);

    /// <summary>A hero's medallion, drawn from a bust portrait (<see cref="KitArtReduction.Medallion"/>).</summary>
    internal static Task<Sprite?> MedallionAsync(Sprite source, KitMedallion look) =>
        Get(source, look);

    private static Task<Sprite?> Get(Sprite source, KitMedallion? look)
    {
        var key = (source.GetInstanceID(), look);
        // A copy that could not be made is made again on the next request: a readback can
        // fail once.
        if (
            Copies.TryGetValue(key, out var entry)
            && entry.Source == source
            && !(entry.Copy.IsCompleted && entry.Copy.Result == null)
        )
            return entry.Copy;
        var copy = MakeAsync(source, look);
        Copies[key] = (source, copy);
        return copy;
    }

    private static async Task<Sprite?> MakeAsync(Sprite source, KitMedallion? look)
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
                look is { } medallion
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
            // The game unloads unused assets between scenes; the cache holds these for the session.
            texture.hideFlags = sprite.hideFlags = HideFlags.DontUnloadUnusedAsset;
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
