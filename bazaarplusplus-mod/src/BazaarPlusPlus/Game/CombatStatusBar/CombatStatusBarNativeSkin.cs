#nullable enable
using BazaarPlusPlus.GameInterop.AssetLoading;
using TheBazaar.AppFramework;
using UnityEngine;
using UnityEngine.UI;

namespace BazaarPlusPlus.Game.CombatStatusBar;

internal sealed class CombatStatusBarNativeSkin
{
    private const string ButtonRoot = "Assets/TheBazaar/Art/UI/Buttons/Btn_Rectangle/";
    private static readonly string[] Addresses =
    [
        ButtonRoot + "Btn_Rct_Frame_S_TUI.png",
        ButtonRoot + "Btn_Rct_Brown_S_Center_Active_TUI.png",
    ];

    private readonly Sprite[] _sprites;

    private CombatStatusBarNativeSkin(Sprite[] sprites) => _sprites = sprites;

    private Sprite Frame => _sprites[0];
    private Sprite Backing => _sprites[1];
    internal bool IsValid => _sprites.All(sprite => sprite != null);

    internal static bool TryBeginLoad(out Task<CombatStatusBarNativeSkin?>? pending)
    {
        pending = null;
        if (!Services.TryGet<AssetLoader>(out var loader) || loader == null)
            return false;

        pending = LoadAsync(loader);
        return true;
    }

    private static async Task<CombatStatusBarNativeSkin?> LoadAsync(AssetLoader loader)
    {
        // These are the game's Addressable sprites, including their atlas and slice borders.
        // The shared loader pins them in Global scope across lobby/run scene transitions.
        if (Addresses.Any(address => !loader.DoesAddressExist(address)))
            return null;

        var sprites = await Task.WhenAll(
            Addresses.Select(address =>
                NativeGlobalAssetLoader.LoadByAddressAsync<Sprite>(loader, address)
            )
        );
        if (sprites.Any(sprite => sprite == null))
            return null;

        return new CombatStatusBarNativeSkin(sprites.Select(sprite => sprite!).ToArray());
    }

    internal void ApplyPlaque(RectTransform rect)
    {
        AddLayer("Backing", rect, Backing, new Vector2(2f, 2f));
        AddLayer("Frame", rect, Frame, Vector2.zero);
    }

    private static void AddLayer(string name, RectTransform parent, Sprite sprite, Vector2 inset)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = inset;
        rect.offsetMax = -inset;
        var image = rect.gameObject.AddComponent<Image>();
        image.sprite = sprite;
        image.color = Color.white;
        image.type = Image.Type.Sliced;
        // Keep the bevel thin at every dock size; stretch only the nine-slice center.
        image.pixelsPerUnitMultiplier = sprite.rect.height / 38f * 100f / sprite.pixelsPerUnit;
        image.raycastTarget = false;
    }
}
