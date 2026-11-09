#nullable enable
using BazaarPlusPlus.Game.HistoryPanel.Ui;
using BazaarPlusPlus.GameInterop.AssetLoading;
using TheBazaar.AppFramework;
using UnityEngine;
using UnityEngine.UI;

namespace BazaarPlusPlus.GameInterop.NativeSkin;

/// <summary>A sprite of the game's rectangle button set that the Desktop UI draws with.</summary>
internal enum DesktopSkinPart
{
    Frame,
    BrownCenter,
    BlueCenter,
}

/// <summary>
/// The Desktop UI's native skin (ADR-0026): the game's <c>Btn_Rectangle</c> sprites, loaded once
/// per process in Global scope so they outlive the lobby and run scopes, and BPP's own sliced
/// header art. A caller keeps its own retry cadence: it calls <see cref="Request"/> when it wants
/// a missing sprite again, and repaints what it drew when <see cref="Generation"/> moves.
/// </summary>
internal static class DesktopNativeSkin
{
    /// <summary>The pixels-per-unit multiplier of a panel's sliced native frame.</summary>
    internal const float PanelPixelsPerUnit = 2.5f;

    private const string ButtonRoot = "Assets/TheBazaar/Art/UI/Buttons/Btn_Rectangle/";
    private static readonly string[] Addresses =
    [
        ButtonRoot + "Btn_Rct_Frame_S_TUI.png",
        ButtonRoot + "Btn_Rct_Brown_S_Center_Active_TUI.png",
        ButtonRoot + "Btn_Rct_Blue_S_Center_Active_TUI.png",
    ];
    private static readonly Sprite?[] Sprites = new Sprite?[Addresses.Length];
    private static Task<Sprite?[]>? _load;
    private static Sprite? _headerNormal;
    private static Sprite? _headerSelected;
    private static bool _headerFailed;

    /// <summary>Moves whenever a sprite changes, so a view repaints the images it drew.</summary>
    internal static int Generation { get; private set; }

    internal static bool IsLoading => _load != null;

    /// <summary>
    /// The exception of the last load that failed, or null when it finished, with or without
    /// every sprite.
    /// </summary>
    internal static Exception? LastLoadError { get; private set; }

    // A sprite the scope release destroyed reads as missing (Unity's ==), so it loads again.
    internal static Sprite? Get(DesktopSkinPart part) =>
        Sprites[(int)part] is { } sprite && sprite != null ? sprite : null;

    internal static bool Has(DesktopSkinPart part) => Get(part) != null;

    /// <summary>
    /// Starts one load when a sprite is missing and none is running; true while a load runs.
    /// False when the skin is whole or the game's asset loader is not up yet.
    /// </summary>
    internal static bool Request()
    {
        Poll();
        if (_load != null)
            return true;
        if (Sprites.All(sprite => sprite != null))
            return false;
        if (!Services.TryGet<AssetLoader>(out var loader) || loader == null)
            return false;
        _load = Addresses.Any(address => !loader.DoesAddressExist(address))
            ? Task.FromResult(new Sprite?[Addresses.Length])
            : Task.WhenAll(
                Addresses.Select(address =>
                    NativeGlobalAssetLoader.LoadByAddressAsync<Sprite>(loader, address)
                )
            );
        return true;
    }

    /// <summary>Takes a finished load's sprites; true when one changed.</summary>
    internal static bool Poll()
    {
        if (_load is not { IsCompleted: true } load)
            return false;
        _load = null;
        if (load.Status != TaskStatus.RanToCompletion)
        {
            LastLoadError = load.Exception;
            return false;
        }
        LastLoadError = null;
        var changed = false;
        for (var i = 0; i < Sprites.Length; i++)
        {
            var sprite = load.Result[i];
            if (sprite != null && sprite != Sprites[i])
            {
                Sprites[i] = sprite;
                changed = true;
            }
        }
        if (changed)
            Generation++;
        return changed;
    }

    /// <summary>BPP's sliced header art for a control at rest; null if it cannot be decoded.</summary>
    internal static Sprite? HeaderNormal => Header(ref _headerNormal, HistoryHeaderArtwork.Normal);

    /// <summary>The same art for a selected control.</summary>
    internal static Sprite? HeaderSelected =>
        Header(ref _headerSelected, HistoryHeaderArtwork.Selected);

    /// <summary>The header art's pixels-per-unit multiplier on the Desktop UI.</summary>
    internal const float HeaderPixelsPerUnit =
        HistoryHeaderArtwork.BorderPixels / HistoryHeaderArtwork.ButtonBorder;

    /// <summary>
    /// A sliced image of <paramref name="part"/> stretched over <paramref name="parent"/> and
    /// inset by <paramref name="inset"/> on every side; it takes no raycast. Until the sprite
    /// loads it is clear, and <see cref="Paint"/> fills it in.
    /// </summary>
    internal static Image Layer(
        Transform parent,
        string name,
        DesktopSkinPart part,
        Color tint,
        float pixelsPerUnitMultiplier,
        Vector2 inset = default
    )
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = inset;
        rect.offsetMax = -inset;
        var image = rect.gameObject.AddComponent<Image>();
        image.type = Image.Type.Sliced;
        image.pixelsPerUnitMultiplier = pixelsPerUnitMultiplier;
        image.raycastTarget = false;
        Paint(image, part, tint);
        return image;
    }

    /// <summary>Puts the part's current sprite on an image, tinted, or clears it while missing.</summary>
    internal static void Paint(Image image, DesktopSkinPart part, Color tint)
    {
        image.sprite = Get(part);
        image.color = image.sprite != null ? tint : Color.clear;
    }

    /// <summary>
    /// The desktop's hover lifts a native button sprite above its own colours. A token holds
    /// channels in 0..1, so this is the one colour the kit writes itself (ADR-0026).
    /// </summary>
    internal static Color OverBrightHover => new(1.12f, 1.12f, 1.12f, 1f);

    // The art lives for the process: it is marked to survive UnloadUnusedAssets and is never
    // destroyed. A sprite something else destroyed decodes again; one that failed stays null.
    private static Sprite? Header(ref Sprite? cache, string name)
    {
        if (cache != null)
            return cache;
        if (_headerFailed)
            return null;
        cache = DecodeHeader(name);
        _headerFailed = cache == null;
        return cache;
    }

    private static Sprite? DecodeHeader(string name)
    {
        using var stream = typeof(DesktopNativeSkin).Assembly.GetManifestResourceStream(
            $"BazaarPlusPlus.Resources.HistoryPanel.{name}"
        );
        if (stream == null)
            return null;
        using var bytes = new MemoryStream();
        stream.CopyTo(bytes);
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false)
        {
            name = name,
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.DontUnloadUnusedAsset,
        };
        if (!texture.LoadImage(bytes.ToArray()))
        {
            UnityEngine.Object.Destroy(texture);
            return null;
        }
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
        texture.Apply(false, true);
        return sprite;
    }
}
