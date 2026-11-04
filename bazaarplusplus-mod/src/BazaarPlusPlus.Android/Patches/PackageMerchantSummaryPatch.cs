using HarmonyLib;
using Il2CppTheBazaar.UI.Tooltips;

namespace BazaarPlusPlus.Android.Patches;

/// <summary>
/// Names the merchant that accepts a package below its passive text, as the desktop's
/// <c>PackageMerchantSummaryTooltipPatch</c> does. A package with no passive text gets no summary.
/// </summary>
[HarmonyPatch(
    typeof(CardTooltipController),
    nameof(CardTooltipController.RenderPassiveEffectTextBlock)
)]
internal static class PackageMerchantSummaryPatch
{
    [HarmonyPrefix]
    private static void Prefix(CardTooltipController __instance, ref string text)
    {
        if (string.IsNullOrEmpty(text))
            return;

        try
        {
            if (
                AndroidPatchHost.EncounterPreview?.RenderPackageMerchant(
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
