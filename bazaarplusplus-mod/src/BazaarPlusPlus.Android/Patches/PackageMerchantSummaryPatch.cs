using HarmonyLib;
using Il2CppTheBazaar.UI.Tooltips;

namespace BazaarPlusPlus.Android.Patches;

[HarmonyPatch(
    typeof(CardTooltipController),
    nameof(CardTooltipController.RenderPassiveEffectTextBlock)
)]
internal static class PackageMerchantSummaryPatch
{
    // First, so the summary sits in the passive text above any preview appended to it.
    [HarmonyPrefix]
    [HarmonyPriority(Priority.High)]
    private static void Prefix(CardTooltipController __instance, ref string text)
    {
        if (string.IsNullOrEmpty(text))
            return;

        try
        {
            if (
                AndroidPatchHost.Features?.EncounterPreview?.RenderPackageMerchant(
                    __instance.CurrentCard,
                    text
                ) is
                { } passive
            )
                text = passive;
        }
        catch (Exception exception)
        {
            AndroidPatchHost.ReportProbeError("package_merchant_summary", exception);
        }
    }
}
