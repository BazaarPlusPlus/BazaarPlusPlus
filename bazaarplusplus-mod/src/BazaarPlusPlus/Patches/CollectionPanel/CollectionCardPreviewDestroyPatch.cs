#nullable enable
#pragma warning disable CS0436
using BazaarPlusPlus.Game.CollectionPanel.Grid;
using HarmonyLib;
using TheBazaar.UI;

namespace BazaarPlusPlus.Patches.CollectionPanel;

// CardPreviewBase.OnDestroy ends with `if (_cardMaterial) Object.Destroy(_cardMaterial)`.
// For tracked collection-panel cards that material is owned by CollectionCardMaterialCache
// and is shared across instances; if the game destroyed it the next card using the same
// artKey would render with a null material. We null the field in a prefix so the original
// destroy branch becomes a no-op; the cache itself destroys all shared materials when the
// panel runtime is torn down.
//
// Also Release the L2 art-cache refcount so the LRU eviction can reclaim entries that no
// longer back any live card.
[HarmonyPatch(typeof(CardPreviewBase), "OnDestroy")]
internal static class CollectionCardPreviewDestroyPatch
{
    [HarmonyPrefix]
    private static void Prefix(CardPreviewBase __instance)
    {
        var marker = __instance.GetComponent<CollectionPanelOwnedMarker>();
        if (marker == null)
            return;

        var artCache = CollectionCardCacheHost.ArtCache;
        var materialCache = CollectionCardCacheHost.MaterialCache;
        if (artCache != null && !string.IsNullOrEmpty(marker.CurrentArtKey))
        {
            artCache.Release(marker.CurrentArtKey!);
            materialCache?.Release(marker.CurrentArtKey!);
            marker.CurrentArtKey = null;
        }

        __instance._cardMaterial = null!;
    }
}
