#nullable enable
using BazaarPlusPlus.GameInterop.HeroPortraits;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.DesktopKit;
using BazaarPlusPlus.Infrastructure.HeroPortraits.Composition;
using BazaarPlusPlus.Infrastructure.Logging;
using UnityEngine;

namespace BazaarPlusPlus.GameInterop.NativeSkin;

// The game's large portraits, trophies and rank badges lack mipmaps and shimmer in small slots.
// Area-average the copies and give them mipmaps.
internal static class KitSmallArt
{
    private const int CapturePixels = 512;
    private const int IconPixels = 128;
    private const int PlatePixels = 176;

    private static readonly Dictionary<
        (int Sprite, KitMedallion? Look),
        (Sprite Source, Task<Sprite?> Copy)
    > Copies = new();

    internal static Task<Sprite?> IconAsync(Sprite source) => Get(source, null);

    internal static Task<Sprite?> MedallionAsync(Sprite source, KitMedallion look) =>
        Get(source, look);

    private static Task<Sprite?> Get(Sprite source, KitMedallion? look)
    {
        var key = (source.GetInstanceID(), look);
        // A readback can fail transiently, so allow the next request to retry.
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
