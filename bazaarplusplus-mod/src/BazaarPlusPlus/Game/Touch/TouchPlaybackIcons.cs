#nullable enable
using BazaarPlusPlus.Mobile.Ui;
using UnityEngine;

namespace BazaarPlusPlus.Game.Touch;

/// <summary>
/// The combat clock's play and pause glyphs: the same play triangle / pause bars as the desktop
/// CombatPlaybackIcon, rasterized once into an ordinary Image sprite instead of injecting
/// a custom Graphic. It decodes no PNG; owned sprites survive scene loads.
/// </summary>
internal sealed class TouchPlaybackIcons : IDisposable
{
    private readonly List<UnityEngine.Object> _owned = new();
    public Sprite Play { get; }
    public Sprite Pause { get; }

    public TouchPlaybackIcons()
    {
        try
        {
            Play = Glyph(true);
            Pause = Glyph(false);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    private Sprite Glyph(bool play)
    {
        const int size = 64;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        Own(_owned, texture);
        var pixels = new Color[size * size];
        for (var y = 0; y < size; y++)
        for (var x = 0; x < size; x++)
        {
            var inside = play
                ? x >= 8 && x <= 56 - Math.Abs(y - 32) * 1.5f
                : y >= 8 && y < 56 && (x >= 10 && x < 24 || x >= 40 && x < 54);
            pixels[y * size + x] = inside ? UiKit.TextColor : Color.clear;
        }
        texture.SetPixels(pixels);
        return GpuOnlySprite(_owned, texture);
    }

    public void Dispose() => DestroyAll(_owned);

    private static Sprite GpuOnlySprite(List<UnityEngine.Object> owned, Texture2D texture)
    {
        texture.filterMode = FilterMode.Bilinear;
        texture.wrapMode = TextureWrapMode.Clamp;
        var sprite = Sprite.Create(
            texture,
            new Rect(0, 0, texture.width, texture.height),
            new Vector2(.5f, .5f)
        );
        Own(owned, sprite);
        texture.Apply(false, true);
        return sprite;
    }

    private static void Own(List<UnityEngine.Object> owned, UnityEngine.Object asset)
    {
        asset.hideFlags = HideFlags.DontUnloadUnusedAsset;
        owned.Add(asset);
    }

    private static void DestroyAll(List<UnityEngine.Object> owned)
    {
        foreach (var asset in owned)
            if (asset != null)
                UnityEngine.Object.Destroy(asset);
        owned.Clear();
    }
}
