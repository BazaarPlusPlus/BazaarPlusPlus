using BazaarPlusPlus.Core.Cards;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using Il2Cpp;
using Il2CppBazaarGameShared.Domain.Core.Types;
using Il2CppTheBazaar.AppFramework;
using Il2CppTheBazaar.Game.CardFrames;
using Il2CppTheBazaar.UI;
using UnityEngine;
using UnityEngine.UI;

namespace BazaarPlusPlus.Android.GameInterop.CardRental;

/// <summary>
/// A rented item on a BPP item board shows its ammo as the game's board does, as on the desktop
/// (<c>NativeCardAmmoBar</c>). The game's card UI prefab has no ammo bar, so the bar comes from
/// the combat recap card, the one item prefab with an <see cref="ItemAmmoController"/> that BPP
/// can load by reference. One copy of that controller is kept hidden as a template. For each
/// card, the template draws the card's tier and maximum ammo with the game's own
/// <c>ResetAmmoView</c>, and a copy of its bar goes on the card, placed by
/// <see cref="AmmoBarPlacement"/>. Main thread only.
/// </summary>
internal static class AndroidCardAmmoBar
{
    private const string BarName = "BPP.AmmoBar";
    private static readonly TimeSpan LoadTimeout = TimeSpan.FromSeconds(10);

    private static Task<ItemAmmoController?>? _template;

    /// <summary>
    /// Starts the template's load on the first call, in the game's Global asset scope. True once
    /// the load ended, with the template or without it; without it, cards show no bar.
    /// </summary>
    public static bool Settled => (_template ??= LoadTemplateAsync()).IsCompleted;

    /// <summary>
    /// After a successful set-up: on an item board, the bar for the card's tier and maximum ammo;
    /// otherwise, or when the card has no ammo, no bar. A pooled card loses the bar it showed.
    /// </summary>
    public static void Show(CardPreviewBase card, bool onBoard)
    {
        Remove(card);
        if (!onBoard || _template is not { IsCompleted: true } load)
            return;
        var template = load.Result;
        var client = card._clientCard;
        if (template == null || card.TryCast<CardPreviewItem>() == null || client == null)
            return;
        var attributes = client.Attributes;
        if (
            attributes == null
            || !attributes.TryGetValue(ECardAttributeType.AmmoMax, out var maxAmmo)
            || maxAmmo <= 0
        )
            return;
        template.ResetAmmoView(client.Tier, maxAmmo);
        var bar = UnityEngine.Object.Instantiate(
            template.ammoBar.gameObject,
            card.transform,
            false
        );
        bar.name = BarName;
        var cardRect = card.GetComponent<RectTransform>();
        Place(
            bar.GetComponent<RectTransform>(),
            cardRect != null ? cardRect.rect.height : NativeCardPrefab.Height
        );
        // The prefab's images ignore masks; on a card they clip where the card clips.
        foreach (var graphic in bar.GetComponentsInChildren<MaskableGraphic>(true))
            graphic.maskable = true;
        foreach (var node in bar.GetComponentsInChildren<Transform>(true))
            node.gameObject.layer = card.gameObject.layer;
    }

    private static void Remove(CardPreviewBase card)
    {
        var bar = card.transform.Find(BarName);
        if (bar == null)
            return;
        // Renamed and hidden now: Destroy runs at the end of the frame, and a set-up in the same
        // frame must not find the old bar.
        bar.name = BarName + ".Removed";
        bar.gameObject.SetActive(false);
        UnityEngine.Object.Destroy(bar.gameObject);
    }

    private static void Place(RectTransform bar, float cardHeight)
    {
        if (cardHeight <= 1f)
            cardHeight = NativeCardPrefab.Height;
        var scale = AmmoBarPlacement.Scale(cardHeight);
        bar.anchorMin = bar.anchorMax = bar.pivot = new Vector2(0.5f, 0.5f);
        bar.anchoredPosition3D = new Vector3(0f, AmmoBarPlacement.CentreY(cardHeight), 0f);
        bar.localRotation = Quaternion.identity;
        bar.localScale = new Vector3(scale, scale, 1f);
        bar.SetAsLastSibling();
    }

    private static async Task<ItemAmmoController?> LoadTemplateAsync()
    {
        try
        {
            if (!Services.TryGet<AssetLoader>(out var loader) || loader == null)
                return Unavailable("asset_loader_missing");
            var prefab = await Il2CppTasks.AwaitAsync(
                loader.LoadAssetAsyncByReference<GameObject>(
                    loader.SmallRecapCardGameplayAssetRef,
                    new Il2CppSystem.Nullable<AssetScope>(AssetScope.Global)
                ),
                "recap card prefab",
                LoadTimeout
            );
            var source =
                prefab != null ? prefab.GetComponentInChildren<ItemAmmoController>(true) : null;
            if (source == null || source.ammoBar == null)
                return Unavailable("ammo_bar_missing");
            var holder = new GameObject("BPP.AmmoBarTemplate");
            holder.SetActive(false);
            UnityEngine.Object.DontDestroyOnLoad(holder);
            return UnityEngine
                .Object.Instantiate(source.gameObject, holder.transform, false)
                .GetComponent<ItemAmmoController>();
        }
        catch (Exception exception)
        {
            return Unavailable("load_failed", exception);
        }
    }

    private static ItemAmmoController? Unavailable(string reason, Exception? exception = null)
    {
        var unavailable = new BppLogEvent(
            BppLogFeatureScope.Android,
            "android.ammo_bar.unavailable"
        );
        if (exception == null)
            BppLog.WarnEvent(unavailable, ("reason_code", reason));
        else
            BppLog.WarnEvent(unavailable, exception, ("reason_code", reason));
        return null;
    }
}
