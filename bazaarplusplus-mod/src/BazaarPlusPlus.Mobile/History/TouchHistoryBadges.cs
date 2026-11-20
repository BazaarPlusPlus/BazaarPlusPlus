using BazaarPlusPlus.Game.HistoryPanel.Ui;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using UnityEngine;

namespace BazaarPlusPlus.Mobile.History;

// Discard atlas CPU pixels with Apply to avoid LoadImage's boxed IL2CPP ReadOnlySpan overload.
// Pin atlases and sprites so a non-additive scene load cannot unload unused badges.
internal sealed class TouchHistoryBadges : IDisposable
{
    private readonly List<UnityEngine.Object> _owned = new();
    private Sprite[] _runStates = [];

    private TouchHistoryBadges() { }

    public Sprite? Win { get; private set; }

    public Sprite? Loss { get; private set; }

    public Sprite? HeaderNormal { get; private set; }

    public Sprite? HeaderSelected { get; private set; }

    public Sprite? RunState(int index) =>
        index >= 0 && index < _runStates.Length ? _runStates[index] : null;

    public static TouchHistoryBadges? TryLoad()
    {
        var badges = new TouchHistoryBadges();
        try
        {
            var results = badges.LoadAtlas("battle-result-badges.png", 2);
            badges.Win = results[0];
            badges.Loss = results[1];
            badges._runStates = badges.LoadAtlas("run-state-badges.png", 3);
            badges.HeaderNormal = badges.LoadAtlas(HistoryHeaderArtwork.Normal, 1, sliced: true)[0];
            badges.HeaderSelected = badges.LoadAtlas(
                HistoryHeaderArtwork.Selected,
                1,
                sliced: true
            )[0];
            return badges;
        }
        catch (Exception exception)
        {
            badges.Dispose();
            BppLog.WarnEvent(
                new BppLogEvent(
                    BppLogFeatureScope.Touch,
                    "touch.history.badges_failed",
                    storm: ["reason_code"]
                ),
                exception,
                ("reason_code", exception.GetType().Name)
            );
            return null;
        }
    }

    public void Dispose()
    {
        foreach (var owned in _owned)
        {
            if (owned != null)
                UnityEngine.Object.Destroy(owned);
        }
        _owned.Clear();
        _runStates = [];
        Win = Loss = null;
        HeaderNormal = HeaderSelected = null;
    }

    private Sprite[] LoadAtlas(string name, int count, bool sliced = false)
    {
        using var stream =
            typeof(TouchHistoryBadges).Assembly.GetManifestResourceStream(
                $"BazaarPlusPlus.Resources.HistoryPanel.{name}"
            ) ?? throw new InvalidOperationException($"History artwork is missing: {name}");
        using var bytes = new MemoryStream();
        stream.CopyTo(bytes);
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false)
        {
            name = name,
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.DontUnloadUnusedAsset,
        };
        _owned.Add(texture);
        if (!ImageConversion.LoadImage(texture, bytes.ToArray()))
            throw new InvalidOperationException($"History artwork could not be decoded: {name}");
        var width = texture.width / (float)count;
        var sprites = new Sprite[count];
        for (var index = 0; index < count; index++)
        {
            sprites[index] = sliced
                ? Sprite.Create(
                    texture,
                    new Rect(index * width, 0, width, texture.height),
                    new Vector2(.5f, .5f),
                    HistoryHeaderArtwork.PixelsPerUnit,
                    0,
                    SpriteMeshType.FullRect,
                    Vector4.one * HistoryHeaderArtwork.BorderPixels
                )
                : Sprite.Create(
                    texture,
                    new Rect(index * width, 0, width, texture.height),
                    new Vector2(.5f, .5f)
                );
            sprites[index].hideFlags = HideFlags.DontUnloadUnusedAsset;
            _owned.Add(sprites[index]);
        }
        texture.Apply(false, true);
        return sprites;
    }
}
