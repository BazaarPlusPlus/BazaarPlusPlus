#pragma warning disable CS0436
#nullable enable
using BazaarPlusPlus.Game.Tooltips;
using BazaarPlusPlus.Patches;
using HarmonyLib;
using TheBazaar;

namespace BazaarPlusPlus.Patches.Tooltips;

[HarmonyPatch(typeof(CardController), "ShowTooltips")]
internal static class UpgradePreviewTooltipPatch
{
    [HarmonyPostfix]
    private static void Postfix(CardController __instance)
    {
        var services = BppPatchHost.Services;
        UpgradeTooltipScheduler.TryScheduleUpgradeTooltip(
            __instance,
            services.Config,
            services.EncounterState
        );
    }
}
