#nullable enable
#pragma warning disable CS0436
using System;
using System.Threading.Tasks;
using BazaarPlusPlus.Game.CollectionPanel.Grid;
using BazaarPlusPlus.Infrastructure;
using HarmonyLib;
using TheBazaar.UI;

namespace BazaarPlusPlus.Patches.CardArtReplacement;

[HarmonyPatch(typeof(CardPreviewItem), "LoadArt")]
internal static class CardPreviewItemArtReplacePatch
{
    private const string LogCategory = "CardArtReplacement";

    [HarmonyPostfix]
    private static void Postfix(CardPreviewItem __instance, ref Task __result)
    {
        var marker = __instance.GetComponent<CollectionPanelOwnedMarker>();
        if (marker == null || __result == null)
            return;

        __result = ApplyAfterLoad(__instance, __result);
    }

    private static async Task ApplyAfterLoad(CardPreviewItem instance, Task loadArtTask)
    {
        await loadArtTask;

        try
        {
            if (instance == null || instance.gameObject == null)
                return;

            if (
                !PackageCardArtPatchGate.TryGetReplacementPreviewMaterial(
                    instance._cardData,
                    instance._cardMaterial,
                    out var customMaterial
                )
            )
                return;

            instance._cardMaterial = customMaterial;
            if (instance._cardImage != null)
                instance._cardImage.material = customMaterial;

            var marker = instance.GetComponent<CollectionPanelOwnedMarker>();
            if (marker != null)
                marker.SharedPreviewMaterial = customMaterial;
        }
        catch (Exception ex)
        {
            BppLog.Warn(LogCategory, $"Preview postfix failed: {ex.Message}");
        }
    }
}
