#nullable enable
#pragma warning disable CS0436
using System;
using System.Threading.Tasks;
using BazaarPlusPlus.Game.CollectionPanel.Grid;
using BazaarPlusPlus.Infrastructure;
using HarmonyLib;
using TheBazaar.UI;

namespace BazaarPlusPlus.Patches.CollectionPanel;

// Replaces CardPreviewItem.LoadArt for cards owned by the collection panel (marked with
// CollectionPanelOwnedMarker). The original loads the CardAssetDataSO straight through
// Addressables every call and synthesises a fresh Material each time — both leak across
// 1146 Item templates. Our replacement consults CollectionCardArtCache for the SO and
// CollectionCardMaterialCache for the Material, sharing both across cards that bind to the
// same artKey. Untagged cards fall through to the original implementation untouched.
[HarmonyPatch(typeof(CardPreviewItem), "LoadArt")]
internal static class CollectionItemLoadArtPatch
{
    [HarmonyPrefix]
    private static bool Prefix(CardPreviewItem __instance, bool isPremium, ref Task __result)
    {
        var marker = __instance.GetComponent<CollectionPanelOwnedMarker>();
        if (marker == null)
            return true;

        var artCache = CollectionCardCacheHost.ArtCache;
        var materialCache = CollectionCardCacheHost.MaterialCache;
        if (artCache == null || materialCache == null)
            return true;

        __result = LoadArtFromCache(__instance, marker, artCache, materialCache);
        return false;
    }

    private static async Task LoadArtFromCache(
        CardPreviewItem instance,
        CollectionPanelOwnedMarker marker,
        CollectionCardArtCache artCache,
        CollectionCardMaterialCache materialCache
    )
    {
        try
        {
            var card = instance._cardData;
            if (card == null)
                return;

            var artKey = card.ArtKey;
            if (
                string.IsNullOrEmpty(artKey)
                || string.Equals(artKey, "Invalid", StringComparison.Ordinal)
            )
            {
                ReleaseCurrentArtKey(marker, artCache, materialCache);
                marker.CurrentArtKey = null;
                return;
            }

            // Release the prior assignment first so refcounts stay accurate when the card is
            // rebound to a different artKey through the pool.
            var changedArtKey = !string.Equals(
                marker.CurrentArtKey,
                artKey,
                StringComparison.Ordinal
            );
            if (changedArtKey)
            {
                ReleaseCurrentArtKey(marker, artCache, materialCache);
                artCache.AddRef(artKey);
                materialCache.Acquire(artKey);
                marker.CurrentArtKey = artKey;
            }

            var assetData = await artCache.Get(artKey);
            if (instance == null)
                return;
            if (assetData == null || assetData.cardMaterial == null)
            {
                if (changedArtKey)
                    ReleaseCurrentArtKey(marker, artCache, materialCache);
                return;
            }

            var material = materialCache.GetOrCreate(
                artKey,
                assetData,
                instance._cardMaterialShader
            );
            if (material == null)
            {
                if (changedArtKey)
                    ReleaseCurrentArtKey(marker, artCache, materialCache);
                return;
            }

            // Shared material across cards: do NOT destroy the previous _cardMaterial — it is
            // either the same shared instance or another shared instance still in use by
            // someone else. The OnDestroy patch nulls _cardMaterial before the game's
            // OnDestroy gets a chance to destroy it (see CollectionCardPreviewDestroyPatch).
            instance._cardMaterial = material;
            if (instance._cardImage != null)
                instance._cardImage.material = material;

            instance._gemGroupController?.Initialize(instance._clientCard);
        }
        catch (Exception ex)
        {
            BppLog.Warn("CollectionItemLoadArtPatch", $"Cached LoadArt failed: {ex.Message}");
        }
    }

    private static void ReleaseCurrentArtKey(
        CollectionPanelOwnedMarker marker,
        CollectionCardArtCache artCache,
        CollectionCardMaterialCache materialCache
    )
    {
        if (!string.IsNullOrEmpty(marker.CurrentArtKey))
        {
            artCache.Release(marker.CurrentArtKey!);
            materialCache.Release(marker.CurrentArtKey!);
            marker.CurrentArtKey = null;
        }
    }
}
