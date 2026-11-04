using HarmonyLib;
using Il2CppTheBazaar.UI.Tooltips;

namespace BazaarPlusPlus.Android.Patches;

[HarmonyPatch(
    typeof(CardTooltipController),
    nameof(CardTooltipController.RenderPassiveEffectTextBlock)
)]
internal static class EncounterPreviewPatch
{
    [HarmonyPrefix]
    private static void Prefix(CardTooltipController __instance, ref string text)
    {
        // As on the desktop, a card with no native passive text shows no preview either.
        if (string.IsNullOrEmpty(text))
            return;

        try
        {
            var content =
                AndroidPatchHost.EncounterPreview?.Render(__instance, text)
                ?? AndroidPatchHost.AggregateItemPreview?.Render(__instance.CurrentCard);
            if (!string.IsNullOrWhiteSpace(content))
                text += "\n<size=75%>" + content + "</size>";
        }
        catch (Exception exception)
        {
            AndroidPatchHost.ReportProbeError("encounter_preview", exception);
        }
    }
}
