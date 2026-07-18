#nullable enable
#pragma warning disable CS0436
using BazaarPlusPlus.Game.CollectionPanel.Grid;
using HarmonyLib;
using TheBazaar.UI;

namespace BazaarPlusPlus.Patches.CollectionPanel;

// CardPreviewBase.OnDestroy ends with `if (_cardMaterial) Object.Destroy(_cardMaterial)`.
// For tracked collection-panel cards that material may be owned by CollectionCardMaterialCache
// and shared across instances; if the game destroyed a shared material the next card using the
// same artKey would render with a null material. We null the field only after the cache patch
// has installed a shared material; native-owned first-setup materials stay in the field so the
// original destroy branch can clean them up.
//
// Also Release the L2 art-cache refcount so the LRU eviction can reclaim entries that no
// longer back any live card.
[HarmonyPatch(typeof(CardPreviewBase), "OnDestroy")]
internal static class CollectionCardPreviewDestroyPatch
{
    [HarmonyPrefix]
    [HarmonyPriority(Priority.Normal)]
    private static void Prefix(CardPreviewBase __instance)
    {
        var marker = __instance.GetComponent<CollectionPanelOwnedMarker>();
        if (marker == null)
            return;

        marker.PreviewOwner?.OnNativeDestroyed(__instance);
    }
}
