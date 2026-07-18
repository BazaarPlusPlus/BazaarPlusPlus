#nullable enable
#pragma warning disable CS0436
using BazaarPlusPlus.Game.CollectionPanel;
using BazaarPlusPlus.Game.CollectionPanel.Grid;
using HarmonyLib;
using TheBazaar.UI;
using Object = UnityEngine.Object;

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

        var cacheSession = marker.CacheOwner ?? CollectionCardCacheHost.ActiveSession;
        if (cacheSession == null)
            return true;

        __result = LoadArtFromCache(__instance, marker, cacheSession);
        return false;
    }

    private static async Task LoadArtFromCache(
        CardPreviewItem instance,
        CollectionPanelOwnedMarker marker,
        CollectionCardCacheSession cacheSession
    )
    {
        string? acquiredArtKey = null;
        var acquiredNewArtRef = false;
        var committedNewArtRef = false;
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
                ClearCachedMaterialAssignment(instance, marker);
                return;
            }

            var hasCurrentRef =
                marker.CacheOwner != null
                && string.Equals(marker.CurrentArtKey, artKey, StringComparison.Ordinal);
            var needsNewRef = !hasCurrentRef;

            var assetData = needsNewRef
                ? await cacheSession.ArtCache.Acquire(artKey)
                : await cacheSession.ArtCache.Get(artKey);
            acquiredNewArtRef = needsNewRef && assetData != null;
            acquiredArtKey = acquiredNewArtRef ? artKey : null;
            if (instance == null)
            {
                if (acquiredNewArtRef)
                {
                    cacheSession.ArtCache.Release(artKey);
                    acquiredNewArtRef = false;
                }
                return;
            }
            if (assetData == null || assetData.cardMaterial == null)
            {
                if (acquiredNewArtRef)
                {
                    cacheSession.ArtCache.Release(artKey);
                    acquiredNewArtRef = false;
                }
                if (needsNewRef)
                    ClearCachedMaterialAssignment(instance, marker);
                return;
            }

            var material = cacheSession.MaterialCache.GetOrCreate(
                artKey,
                assetData,
                instance._cardMaterialShader
            );
            if (material == null)
            {
                if (acquiredNewArtRef)
                {
                    cacheSession.ArtCache.Release(artKey);
                    acquiredNewArtRef = false;
                }
                if (needsNewRef)
                    ClearCachedMaterialAssignment(instance, marker);
                return;
            }

            if (needsNewRef)
            {
                cacheSession.MaterialCache.Acquire(artKey);
                marker.ReleaseCurrentArtKey();
                marker.CacheOwner = cacheSession;
                marker.CurrentArtKey = artKey;
                committedNewArtRef = true;
                acquiredNewArtRef = false;
            }

            if (
                !marker.CardMaterialOwnedByCache
                && instance._cardMaterial != null
                && !ReferenceEquals(instance._cardMaterial, material)
            )
            {
                Object.Destroy(instance._cardMaterial);
            }

            instance._cardMaterial = material;
            marker.CardMaterialOwnedByCache = true;
            if (instance._cardImage != null)
                instance._cardImage.material = material;

            instance._gemGroupController?.Initialize(instance._clientCard);
        }
        catch (Exception ex)
        {
            if (acquiredNewArtRef && !committedNewArtRef && !string.IsNullOrEmpty(acquiredArtKey))
                cacheSession.ArtCache.Release(acquiredArtKey!);
            cacheSession.ArtCache.ReportDegraded(
                CollectionPanelLogReasonCode.CachedLoadFailed,
                acquiredArtKey ?? marker.CurrentArtKey,
                ex
            );
        }
    }

    private static void ClearCachedMaterialAssignment(
        CardPreviewItem instance,
        CollectionPanelOwnedMarker marker
    )
    {
        if (!marker.CardMaterialOwnedByCache && instance._cardMaterial != null)
        {
            Object.Destroy(instance._cardMaterial);
        }

        instance._cardMaterial = null!;
        if (instance._cardImage != null)
            instance._cardImage.material = null;
        marker.CardMaterialOwnedByCache = false;

        marker.ReleaseCurrentArtKey();
    }
}
