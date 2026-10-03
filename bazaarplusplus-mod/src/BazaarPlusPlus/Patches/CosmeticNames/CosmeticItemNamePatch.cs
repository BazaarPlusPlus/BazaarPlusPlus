#nullable enable
using BazaarPlusPlus.Game.CosmeticNames;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using HarmonyLib;
using TheBazaar;

namespace BazaarPlusPlus.Patches.CosmeticNames;

// CosmeticButton is the category selector; only CosmeticItem represents an individual item.
[HarmonyPatch(typeof(CosmeticItem), nameof(CosmeticItem.SetData))]
internal static class CosmeticItemNamePatch
{
    [HarmonyPostfix]
    private static void Postfix(CosmeticItem __instance, BazaarSaleItem data, bool isPlaceHolder)
    {
        try
        {
            var overlay =
                __instance.GetComponent<CosmeticNameOverlay>()
                ?? __instance.gameObject.AddComponent<CosmeticNameOverlay>();
            overlay.Initialize(BppPatchHost.Services.Config, __instance, data, isPlaceHolder);
        }
        catch (Exception ex)
        {
            BppLog.WarnEvent(
                new BppLogEvent(
                    BppLogFeatureScope.CosmeticNames,
                    "cosmetic_names.overlay.degraded",
                    storm: []
                ),
                ex
            );
        }
    }
}
