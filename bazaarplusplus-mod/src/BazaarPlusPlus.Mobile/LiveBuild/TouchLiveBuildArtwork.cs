using BazaarPlusPlus.Game.HistoryPanel.Ui;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using UnityEngine;

namespace BazaarPlusPlus.Mobile.LiveBuild;

/// <summary>History's sliced control artwork for the Ten-Win touch surface.</summary>
internal sealed class TouchLiveBuildArtwork : IDisposable
{
    private readonly List<UnityEngine.Object> _owned = new();

    private TouchLiveBuildArtwork() { }

    internal Sprite? HeaderNormal { get; private set; }

    internal Sprite? HeaderSelected { get; private set; }

    internal static TouchLiveBuildArtwork? TryLoad()
    {
        var artwork = new TouchLiveBuildArtwork();
        try
        {
            artwork.HeaderNormal = artwork.Load(HistoryHeaderArtwork.Normal);
            artwork.HeaderSelected = artwork.Load(HistoryHeaderArtwork.Selected);
            return artwork;
        }
        catch (Exception exception)
        {
            artwork.Dispose();
            BppLog.WarnEvent(
                new BppLogEvent(
                    BppLogFeatureScope.Touch,
                    "touch.live_build.artwork_failed",
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
        foreach (var asset in _owned)
            if (asset != null)
                UnityEngine.Object.Destroy(asset);
        _owned.Clear();
        HeaderNormal = HeaderSelected = null;
    }

    private Sprite Load(string name)
    {
        using var stream =
            typeof(TouchLiveBuildArtwork).Assembly.GetManifestResourceStream(
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
        var sprite = Sprite.Create(
            texture,
            new Rect(0, 0, texture.width, texture.height),
            new Vector2(.5f, .5f),
            HistoryHeaderArtwork.PixelsPerUnit,
            0,
            SpriteMeshType.FullRect,
            Vector4.one * HistoryHeaderArtwork.BorderPixels
        );
        sprite.hideFlags = HideFlags.DontUnloadUnusedAsset;
        _owned.Add(sprite);
        texture.Apply(false, true);
        return sprite;
    }
}
