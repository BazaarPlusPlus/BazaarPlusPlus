using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace BazaarPlusPlus.Android.Ui;

/// <summary>Panel and playback artwork; owned sprites survive scene loads.</summary>
internal sealed class AndroidControlIcons : IDisposable
{
    private readonly List<UnityEngine.Object> _owned = new();
    public Sprite Collection { get; }
    public Sprite History { get; }
    public Sprite TenWins { get; }
    public Sprite Play { get; }
    public Sprite Pause { get; }

    public AndroidControlIcons()
    {
        try
        {
            Collection = Load("Controls.collection.png");
            History = Load("Controls.history.png");
            TenWins = Load("Controls.ten-win-builds.png");
            Play = PlaybackGlyph(true);
            Pause = PlaybackGlyph(false);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    private Sprite Load(string resource)
    {
        using var stream =
            typeof(AndroidControlIcons).Assembly.GetManifestResourceStream(
                "BazaarPlusPlus.Android.Resources." + resource
            ) ?? throw new InvalidOperationException("Missing control artwork: " + resource);
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        Own(texture);
        if (!ImageConversion.LoadImage(texture, new Il2CppStructArray<byte>(buffer.ToArray())))
            throw new InvalidOperationException("Cannot decode control artwork: " + resource);
        return Sprite(texture, texture.width);
    }

    // The same play triangle / pause bars as the desktop CombatPlaybackIcon, rasterized
    // once for IL2CPP's ordinary Image component instead of injecting a custom Graphic.
    private Sprite PlaybackGlyph(bool play)
    {
        const int size = 64;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        Own(texture);
        var pixels = new Il2CppStructArray<Color>(size * size * 1L);
        for (var y = 0; y < size; y++)
        for (var x = 0; x < size; x++)
        {
            var inside = play
                ? x >= 8 && x <= 56 - Math.Abs(y - 32) * 1.5f
                : y >= 8 && y < 56 && (x >= 10 && x < 24 || x >= 40 && x < 54);
            pixels[y * size + x] = inside ? AndroidUiKit.TextColor : Color.clear;
        }
        texture.SetPixels(pixels);
        texture.Apply();
        return Sprite(texture, size);
    }

    private Sprite Sprite(Texture2D texture, int width)
    {
        texture.filterMode = FilterMode.Bilinear;
        texture.wrapMode = TextureWrapMode.Clamp;
        var sprite = UnityEngine.Sprite.Create(
            texture,
            new Rect(0, 0, width, texture.height),
            new Vector2(.5f, .5f)
        );
        Own(sprite);
        return sprite;
    }

    private void Own(UnityEngine.Object asset)
    {
        asset.hideFlags = HideFlags.DontUnloadUnusedAsset;
        _owned.Add(asset);
    }

    public void Dispose()
    {
        foreach (var asset in _owned)
            if (asset != null)
                UnityEngine.Object.Destroy(asset);
        _owned.Clear();
    }
}
