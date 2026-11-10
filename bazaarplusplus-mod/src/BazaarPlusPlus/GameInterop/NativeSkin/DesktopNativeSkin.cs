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
/// A sprite the Desktop UI kit draws with (ADR-0027). The plates are BPP's own art, embedded in
/// the plugin; the rule and the glyphs are the game's.
/// </summary>
internal enum DesktopKitArt
{
    /// <summary>The plates' silhouette in solid white, tinted: hover washes.</summary>
    Shape,

    /// <summary>A control's plate at rest, under the pointer and pressed.</summary>
    Plate,
    PlateHover,
    PlatePressed,

    /// <summary>The primary button's plate at rest and under the pointer.</summary>
    PrimaryPlate,
    PrimaryPlateHover,

    /// <summary>The destructive confirm's plate in three states.</summary>
    DangerPlate,
    DangerPlateHover,
    DangerPlatePressed,

    /// <summary>A soft gold line along the plates' silhouette, and its brighter form.</summary>
    Edge,
    EdgeBright,

    /// <summary>The ground of a rail, a well or a floating surface.</summary>
    Ground,

    /// <summary>The fading rule the game draws between match-history runs.</summary>
    Divider,
    IconClose,
    IconSearch,
    IconArrow,

    /// <summary>
    /// BPP's painted state marks: a finished run below a trophy, an abandoned run, a run in
    /// progress, a battle won and a battle lost.
    /// </summary>
    MarkCompleted,
    MarkAbandoned,
    MarkActive,
    MarkWon,
    MarkLost,
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

    // The Desktop UI kit's art (ADR-0027), in DesktopKitArt order: an embedded PNG of BPP's own
    // (a file name), or a game sprite (an address). Each game sprite loads on its own, so a game
    // build without one leaves the others and the frame and centre sprites above untouched.
    private const string HistoryRoot = "Assets/TheBazaar/Art/UI/Buttons/Btn_MatchHistory/";
    private const string IconRoot = "Assets/TheBazaar/Art/UI/Icons/";
    private const string OwnedPrefix = "BazaarPlusPlus.Resources.DesktopKit.";
    private static readonly string[] KitSources =
    [
        "shape.png",
        "plate.png",
        "plate-hover.png",
        "plate-pressed.png",
        "plate-primary.png",
        "plate-primary-hover.png",
        "plate-danger.png",
        "plate-danger-hover.png",
        "plate-danger-pressed.png",
        "edge.png",
        "edge-bright.png",
        "ground.png",
        HistoryRoot + "Btn_MatchHistory_Run_Common_Divider_TUI.png",
        IconRoot + "Icon_RingBtn_X_TUI.png",
        IconRoot + "Icon_RingBtn_Search_TUI.png",
        IconRoot + "Menus/Icon_Menu_Dropdown_02_TUI.png",
        "mark-completed.png",
        "mark-abandoned.png",
        "mark-active.png",
        "mark-won.png",
        "mark-lost.png",
    ];
    private static readonly Sprite?[] KitSprites = new Sprite?[KitSources.Length];
    private static readonly Task<Sprite?>?[] KitLoads = new Task<Sprite?>?[KitSources.Length];
    private static readonly string?[] KitProblems = new string?[KitSources.Length];
    private static Task<Sprite?[]>? _load;
    private static Sprite? _headerNormal;
    private static Sprite? _headerSelected;
    private static bool _headerFailed;

    /// <summary>
    /// The owned plates' pixels per canvas unit (and so their pixels-per-unit multiplier): a
    /// plate PNG is one md control tall at this scale.
    /// </summary>
    internal const float KitPixelsPerUnit = 3f;

    /// <summary>
    /// The sliced border of the owned plates, in pixels: the silhouette's corner and its rim.
    /// </summary>
    internal const float KitBorderPixels = 30f;

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

    /// <summary>A sprite of the kit's art, or null while it is loading or missing.</summary>
    internal static Sprite? KitArt(DesktopKitArt art)
    {
        var index = (int)art;
        if (KitSprites[index] is { } sprite && sprite != null)
            return sprite;
        if (!IsOwned(index) || KitProblems[index] != null)
            return null;
        KitSprites[index] = DecodeOwned(KitSources[index]);
        if (KitSprites[index] == null)
            KitProblems[index] = "decode-failed";
        return KitSprites[index];
    }

    /// <summary>Why a kit sprite is missing: null while it loads or once it is in.</summary>
    internal static string? KitArtProblem(DesktopKitArt art) => KitProblems[(int)art];

    /// <summary>A kit sprite's file name, for the Kit Gallery's diagnostics.</summary>
    internal static string KitArtName(DesktopKitArt art) =>
        Path.GetFileNameWithoutExtension(KitSources[(int)art]);

    private static bool IsOwned(int index) => !KitSources[index].Contains('/');

    /// <summary>
    /// Starts a load of every game sprite of the kit that is missing and not loading; true while
    /// one runs. A sprite whose address the build lacks, or whose load failed, is remembered
    /// with the reason and never asked again. The owned plates need no load.
    /// </summary>
    internal static bool RequestKitArt()
    {
        var loading = false;
        AssetLoader? loader = null;
        for (var index = 0; index < KitSources.Length; index++)
        {
            if (IsOwned(index))
                continue;
            PollKitArt(index);
            if (KitLoads[index] != null)
            {
                loading = true;
                continue;
            }
            if (
                KitArt((DesktopKitArt)index) != null
                || KitProblems[index] is "missing-address" or "load-failed"
            )
                continue;
            if (loader == null && (!Services.TryGet(out loader) || loader == null))
            {
                KitProblems[index] = "loader-not-ready";
                continue;
            }
            if (!loader.DoesAddressExist(KitSources[index]))
            {
                KitProblems[index] = "missing-address";
                continue;
            }
            KitProblems[index] = null;
            KitLoads[index] = NativeGlobalAssetLoader.LoadByAddressAsync<Sprite>(
                loader,
                KitSources[index]
            );
            loading = true;
        }
        return loading;
    }

    private static void PollKitArt(int index)
    {
        if (KitLoads[index] is not { IsCompleted: true } load)
            return;
        KitLoads[index] = null;
        if (load.Status != TaskStatus.RanToCompletion || load.Result == null)
        {
            KitProblems[index] = "load-failed";
            return;
        }
        if (load.Result == KitSprites[index])
            return;
        KitSprites[index] = load.Result;
        Generation++;
    }

    // An owned sprite lives for the process, like the header art below. A plate is sliced; a
    // mark is not.
    private static Sprite? DecodeOwned(string file) =>
        Decode(
            OwnedPrefix + file,
            file,
            100f,
            file.StartsWith("mark-", StringComparison.Ordinal) ? 0f : KitBorderPixels
        );

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

    private static Sprite? DecodeHeader(string name) =>
        Decode(
            $"BazaarPlusPlus.Resources.HistoryPanel.{name}",
            name,
            HistoryHeaderArtwork.PixelsPerUnit,
            HistoryHeaderArtwork.BorderPixels
        );

    private static Sprite? Decode(string resource, string name, float pixelsPerUnit, float border)
    {
        using var stream = typeof(DesktopNativeSkin).Assembly.GetManifestResourceStream(resource);
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
            pixelsPerUnit,
            0,
            SpriteMeshType.FullRect,
            Vector4.one * border
        );
        sprite.hideFlags = HideFlags.DontUnloadUnusedAsset;
        texture.Apply(false, true);
        return sprite;
    }
}
