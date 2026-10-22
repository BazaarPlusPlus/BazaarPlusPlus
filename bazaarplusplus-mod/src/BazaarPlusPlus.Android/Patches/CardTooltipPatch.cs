using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using HarmonyLib;

namespace BazaarPlusPlus.Android.Patches;

/// <summary>
/// Counts card tooltips, which a tap opens on touch screens. <c>CardController</c> lives in the
/// global namespace of the game, so its proxy is <c>Il2Cpp.CardController</c>.
/// </summary>
[HarmonyPatch(typeof(Il2Cpp.CardController), nameof(Il2Cpp.CardController.ShowTooltips))]
internal static class CardTooltipPatch
{
    private const long LoggedHits = 5;

    [HarmonyPrefix]
    private static void Prefix()
    {
        var hits = AndroidPatchHost.CountTooltip();
        if (hits <= LoggedHits)
            BppLog.InfoEvent(
                new BppLogEvent(BppLogFeatureScope.Android, "android.tooltip.hit"),
                ("hits", hits)
            );
    }
}
