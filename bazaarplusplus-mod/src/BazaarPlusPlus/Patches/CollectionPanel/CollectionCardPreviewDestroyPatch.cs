#nullable enable
#pragma warning disable CS0436
using BazaarPlusPlus.Game.CollectionPanel.Grid;
using HarmonyLib;
using TheBazaar.UI;

namespace BazaarPlusPlus.Patches.CollectionPanel;

// Native OnDestroy destroys _cardMaterial. Clear the field only for cached materials
// so it cannot destroy a shared material another preview still uses.
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
