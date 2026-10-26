using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace BazaarPlusPlus.Android.Ui;

/// <summary>
/// The touch shortcuts' artwork (Collection, History, Ten-Win Builds), decoded from the PNGs
/// linked into this assembly. Owned sprites survive scene loads, and each texture keeps no CPU
/// copy of its pixels once its sprite exists.
/// </summary>
internal sealed class AndroidControlIcons : IDisposable
{
    private readonly List<UnityEngine.Object> _owned = new();
    public Sprite Collection { get; }
    public Sprite History { get; }
    public Sprite TenWins { get; }

    public AndroidControlIcons()
    {
        try
        {
            Collection = Load("Controls.collection.png");
            History = Load("Controls.history.png");
            TenWins = Load("Controls.ten-win-builds.png");
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
        Own(_owned, texture);
        if (!ImageConversion.LoadImage(texture, new Il2CppStructArray<byte>(buffer.ToArray())))
            throw new InvalidOperationException("Cannot decode control artwork: " + resource);
        return GpuOnlySprite(_owned, texture);
    }

    /// <summary>
    /// A sprite over all of <paramref name="texture"/>; then the texture is uploaded without a
    /// CPU copy. The sprite is cut first because its mesh is read from those pixels.
    /// </summary>
    internal static Sprite GpuOnlySprite(List<UnityEngine.Object> owned, Texture2D texture)
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

    internal static void Own(List<UnityEngine.Object> owned, UnityEngine.Object asset)
    {
        asset.hideFlags = HideFlags.DontUnloadUnusedAsset;
        owned.Add(asset);
    }

    internal static void DestroyAll(List<UnityEngine.Object> owned)
    {
        foreach (var asset in owned)
            if (asset != null)
                UnityEngine.Object.Destroy(asset);
        owned.Clear();
    }

    public void Dispose() => DestroyAll(_owned);
}

/// <summary>
/// The combat clock's play and pause glyphs: the same play triangle / pause bars as the desktop
/// CombatPlaybackIcon, rasterized once for IL2CPP's ordinary Image component instead of injecting
/// a custom Graphic. It decodes no PNG; owned sprites survive scene loads.
/// </summary>
internal sealed class AndroidPlaybackIcons : IDisposable
{
    private readonly List<UnityEngine.Object> _owned = new();
    public Sprite Play { get; }
    public Sprite Pause { get; }

    public AndroidPlaybackIcons()
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
        AndroidControlIcons.Own(_owned, texture);
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
        return AndroidControlIcons.GpuOnlySprite(_owned, texture);
    }

    public void Dispose() => AndroidControlIcons.DestroyAll(_owned);
}
