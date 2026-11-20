#nullable enable
using BazaarPlusPlus.GameInterop.AssetLoading;
using TheBazaar.AppFramework;
using UnityEngine;
using UnityEngine.UI;

namespace BazaarPlusPlus.GameInterop.NativeSkin;

internal enum DesktopSkinPart
{
    Frame,
    BrownCenter,
    BlueCenter,
}

internal enum DesktopKitArt
{
    Shape,

    Plate,
    PlateHover,
    PlatePressed,

    PrimaryPlate,
    PrimaryPlateHover,

    DangerPlate,
    DangerPlateHover,
    DangerPlatePressed,

    Edge,
    EdgeBright,

    Ground,

    Divider,
    IconClose,
    IconSearch,
    IconArrow,

    MarkCompleted,
    MarkAbandoned,
    MarkActive,
    MarkWon,
    MarkLost,

    IconHeart,
}

// Load the native skin in Global scope so its sprites survive lobby and run scope changes.
internal static class DesktopNativeSkin
{
    internal const float PanelPixelsPerUnit = 2.5f;

    private const string ButtonRoot = "Assets/TheBazaar/Art/UI/Buttons/Btn_Rectangle/";
    private static readonly string[] Addresses =
    [
        ButtonRoot + "Btn_Rct_Frame_S_TUI.png",
        ButtonRoot + "Btn_Rct_Brown_S_Center_Active_TUI.png",
        ButtonRoot + "Btn_Rct_Blue_S_Center_Active_TUI.png",
    ];
    private static readonly Sprite?[] Sprites = new Sprite?[Addresses.Length];

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
        "icon-heart.png",
    ];
    private static readonly Sprite?[] KitSprites = new Sprite?[KitSources.Length];
    private static readonly Task<Sprite?>?[] KitLoads = new Task<Sprite?>?[KitSources.Length];
    private static readonly string?[] KitProblems = new string?[KitSources.Length];
    private static Task<Sprite?[]>? _load;

    // At this scale each owned plate PNG is one medium control tall.
    internal const float KitPixelsPerUnit = 3f;

    internal const float KitBorderPixels = 30f;

    internal static int Generation { get; private set; }

    internal static bool IsLoading => _load != null;

    internal static Exception? LastLoadError { get; private set; }

    // A sprite the scope release destroyed reads as missing (Unity's ==), so it loads again.
    internal static Sprite? Get(DesktopSkinPart part) =>
        Sprites[(int)part] is { } sprite && sprite != null ? sprite : null;

    internal static bool Has(DesktopSkinPart part) => Get(part) != null;

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

    internal static string? KitArtProblem(DesktopKitArt art) => KitProblems[(int)art];

    internal static string KitArtName(DesktopKitArt art) =>
        Path.GetFileNameWithoutExtension(KitSources[(int)art]);

    private static bool IsOwned(int index) => !KitSources[index].Contains('/');

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

    // Owned sprites must survive UnloadUnusedAssets for the lifetime of the process.
    private static Sprite? DecodeOwned(string file) =>
        Decode(
            OwnedPrefix + file,
            file,
            100f,
            file.StartsWith("mark-", StringComparison.Ordinal)
            || file.StartsWith("icon-", StringComparison.Ordinal)
                ? 0f
                : KitBorderPixels
        );

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

    internal static void Paint(Image image, DesktopSkinPart part, Color tint)
    {
        image.sprite = Get(part);
        image.color = image.sprite != null ? tint : Color.clear;
    }

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
