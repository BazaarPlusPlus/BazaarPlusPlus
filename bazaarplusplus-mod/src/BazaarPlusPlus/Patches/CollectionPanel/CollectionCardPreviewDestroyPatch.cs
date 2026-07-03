#nullable enable
#pragma warning disable CS0436
using BazaarPlusPlus.Game.CollectionPanel.Grid;
using HarmonyLib;
using TheBazaar.UI;

namespace BazaarPlusPlus.Patches.CollectionPanel;

// CardPreviewBase.OnDestroy ends with `if (_cardMaterial) Object.Destroy(_cardMaterial)`.
// Collection cards normally let the game's native material lifecycle run unchanged. The only
// exception is BPP custom package-card preview material: CardArtReplacementFeature owns that
// shared material cache, so collection-owned previews must not destroy those cached instances.
[HarmonyPatch(typeof(CardPreviewBase), "OnDestroy")]
internal static class CollectionCardPreviewDestroyPatch
{
    [HarmonyPrefix]
    private static void Prefix(CardPreviewBase __instance)
    {
        var marker = __instance.GetComponent<CollectionPanelOwnedMarker>();
        if (marker == null)
            return;

        if (
            marker.SharedPreviewMaterial != null
            && ReferenceEquals(__instance._cardMaterial, marker.SharedPreviewMaterial)
        )
            __instance._cardMaterial = null!;
        marker.SharedPreviewMaterial = null;
    }
}
