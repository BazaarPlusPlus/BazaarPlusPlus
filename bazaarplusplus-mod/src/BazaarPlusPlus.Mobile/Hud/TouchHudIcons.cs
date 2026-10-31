using BazaarPlusPlus.Mobile.Ui;
using UnityEngine;

namespace BazaarPlusPlus.Mobile.Hud;

/// <summary>
/// The touch shortcuts' artwork (Collection, Ten-Win Builds) and History's menu icon, decoded
/// from the desktop's own PNGs linked into the host assembly under the Mono host's names. Owned
/// sprites survive scene loads, and each texture keeps no CPU copy of its pixels once its sprite
/// exists.
/// </summary>
internal sealed class TouchHudIcons : IDisposable
{
    private readonly List<UnityEngine.Object> _owned = new();

    public Sprite Collection { get; }

    public Sprite TenWins { get; }

    /// <summary>History's tile in the game's menus (the Android host's).</summary>
    public Sprite History { get; }

    public TouchHudIcons()
    {
        try
        {
            Collection = Load("DockButtons.collection-panel-icon.png");
            TenWins = Load("DockButtons.ten-win-builds-icon.png");
            History = Load("HistoryPanel.history-menu.png");
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
            typeof(TouchHudIcons).Assembly.GetManifestResourceStream(
                "BazaarPlusPlus.Resources." + resource
            ) ?? throw new InvalidOperationException("Missing control artwork: " + resource);
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        Own(_owned, texture);
        if (!ImageConversion.LoadImage(texture, buffer.ToArray()))
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
/// CombatPlaybackIcon, rasterized once into an ordinary Image sprite instead of a custom
/// Graphic, which an IL2CPP host cannot inject. It decodes no PNG; owned sprites survive scene
/// loads.
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
        TouchHudIcons.Own(_owned, texture);
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
        return TouchHudIcons.GpuOnlySprite(_owned, texture);
    }

    public void Dispose() => TouchHudIcons.DestroyAll(_owned);
}
