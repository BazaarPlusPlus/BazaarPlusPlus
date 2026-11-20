#nullable enable
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Core.Cards;
using BazaarPlusPlus.GameInterop.AssetLoading;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using TheBazaar.AppFramework;
using TheBazaar.Game.CardFrames;
using TheBazaar.UI;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace BazaarPlusPlus.GameInterop.Cards;

// CardPreviewItem has no ammo bar. Borrow the combat recap prefab's ItemAmmoController,
// the only loadable one, to match the native board's out-of-combat display. Main thread only.
internal static class NativeCardAmmoBar
{
    private const string BarName = "BPP.AmmoBar";

    private static Task<ItemAmmoController?>? _template;

    public static Task<ItemAmmoController?> TemplateAsync() => _template ??= LoadTemplateAsync();

    public static void Show(CardPreviewBase card, ItemAmmoController? template)
    {
        Remove(card);
        if (template == null || card is not CardPreviewItem || card._clientCard == null)
            return;
        var attributes = card._clientCard.Attributes;
        if (
            attributes == null
            || !attributes.TryGetValue(ECardAttributeType.AmmoMax, out var maxAmmo)
            || maxAmmo <= 0
        )
            return;
        template.ResetAmmoView(card._clientCard.Tier, maxAmmo);
        var bar = Object.Instantiate(template.ammoBar.gameObject, card.transform, false);
        bar.name = BarName;
        Place((RectTransform)bar.transform, card.ParentRect.rect.height);
        // The prefab's images ignore masks; on a card they clip where the card clips.
        foreach (var graphic in bar.GetComponentsInChildren<MaskableGraphic>(true))
            graphic.maskable = true;
        foreach (var node in bar.GetComponentsInChildren<Transform>(true))
            node.gameObject.layer = card.gameObject.layer;
    }

    public static void Remove(CardPreviewBase card)
    {
        if (card == null)
            return;
        var bar = card.transform.Find(BarName);
        if (bar == null)
            return;
        // Renamed and hidden now: Destroy runs at the end of the frame, and a set-up in the same
        // frame must not find the old bar.
        bar.name = BarName + ".Removed";
        bar.gameObject.SetActive(false);
        Object.Destroy(bar.gameObject);
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
            var prefab = await NativeGlobalAssetLoader.LoadByReferenceAsync<GameObject>(
                loader,
                loader.SmallRecapCardGameplayAssetRef
            );
            var source =
                prefab != null ? prefab.GetComponentInChildren<ItemAmmoController>(true) : null;
            if (source == null || source.ammoBar == null)
                return Unavailable("ammo_bar_missing");
            var holder = new GameObject("BPP.AmmoBarTemplate");
            holder.SetActive(false);
            Object.DontDestroyOnLoad(holder);
            return Object
                .Instantiate(source.gameObject, holder.transform, false)
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
            BppLogFeatureScope.StaticCards,
            "static_cards.ammo_bar.unavailable"
        );
        if (exception == null)
            BppLog.WarnEvent(unavailable, ("reason_code", reason));
        else
            BppLog.WarnEvent(unavailable, exception, ("reason_code", reason));
        return null;
    }
}
