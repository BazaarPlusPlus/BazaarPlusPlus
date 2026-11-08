using BazaarPlusPlus.Android.GameInterop;
using BazaarPlusPlus.Android.Host;
using BazaarPlusPlus.Game.EventPreview;
using BazaarPlusPlus.Mobile.Previews;
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
            var features = AndroidPatchHost.Features;
            var content =
                Encounter(__instance, text)
                ?? features?.AggregateItemPreview?.Render(__instance.CurrentCard);
            if (!string.IsNullOrWhiteSpace(content))
                text += "\n" + TouchPreviewTextSize.Wrap(content, PreviewTextPercent(features));
        }
        catch (Exception exception)
        {
            AndroidPatchHost.ReportProbeError("encounter_preview", exception);
        }
    }

    internal static int PreviewTextPercent(AndroidInteractionFeatures? features) =>
        features?.PreviewTextSize.Value ?? TouchPreviewTextSize.DefaultPercent;

    private static string? Encounter(CardTooltipController controller, string text)
    {
        var card = controller.CurrentCard;
        if (
            AndroidPatchHost.Features?.EncounterPreview is not { } preview
            || card == null
            || !Guid.TryParse(card.TemplateId.ToString(), out var id)
            || !AndroidCardFacets.Type.TryToFacet(card.Type, out var type)
        )
            return null;
        var result = preview.Module.ResolveCard(new EncounterCardPreviewQuery(id, type, text));
        return result.Availability == EventPreviewAvailability.Available ? result.Content : null;
    }
}
