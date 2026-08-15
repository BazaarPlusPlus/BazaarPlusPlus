#nullable enable
using BazaarPlusPlus.Game.BilingualItemNames;
using BazaarPlusPlus.GameInterop.Localization;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Localization;
using HarmonyLib;
using TheBazaar.Tooltips;
using TheBazaar.UI.Tooltips;

namespace BazaarPlusPlus.Patches.Tooltips;

// Native CardTooltipTypeHandler renders the localized title before positioning the tooltip.
// Append the official zh-CN title at that seam so layout includes the subtitle and the active
// game locale never needs to be switched.
[HarmonyPatch(typeof(CardTooltipTypeHandler), "RenderCardUI")]
internal static class BilingualItemNamePatch
{
    [HarmonyPostfix]
    private static void Postfix(CardTooltipController controller, CardTooltipData tooltipData)
    {
        try
        {
            var card = tooltipData.CardInstance;
            var enabled =
                BppPatchHost.Services.Config.EnableBilingualItemNamesConfig?.Value ?? false;
            var currentLanguageIsChinese = LanguageCodeMatcher.IsChinese(L.CurrentLanguageCode);
            var supportedCard = card != null && BilingualNameCardEligibility.IsSupported(card.Type);
            if (!enabled || !supportedCard)
            {
                BilingualItemNameSubtitle.Hide(controller);
                return;
            }

            var titleToken = tooltipData.CardTemplate.Localization?.Title;
            var secondaryTitle = currentLanguageIsChinese
                ? titleToken?.Text
                : ChineseTranslationCatalog.TryResolve(titleToken);
            var subtitle = BilingualItemNamePresentation.TryBuildSubtitle(
                controller.headerText?.text,
                secondaryTitle,
                enabled,
                isSupportedCard: true
            );
            if (subtitle == null)
            {
                BilingualItemNameSubtitle.Hide(controller);
                return;
            }

            // The secondary title is English only when the active game locale is Chinese.
            if (!BilingualItemNameSubtitle.TryShow(controller, subtitle, currentLanguageIsChinese))
            {
                BilingualItemNameSubtitle.Hide(controller);
                return;
            }
        }
        catch (Exception ex)
        {
            BppLog.WarnEvent(
                BilingualItemNamesLogEvents.TooltipDegraded,
                ex,
                BilingualItemNamesLogEvents.TooltipDegradedReasonCode.Bind(
                    BilingualLogReasonCode.TooltipPatchException
                )
            );
        }
    }
}

// Hero-level and PVP-opponent tooltips reuse the same pooled CardTooltipController but bypass
// CardTooltipTypeHandler.RenderCardUI. Clear the BPP-owned subtitle at their shared native reset
// seam so the previous card's translated name cannot leak beneath a non-card header.
[HarmonyPatch(typeof(CardTooltipController), nameof(CardTooltipController.ResetValues))]
internal static class BilingualItemNameResetPatch
{
    [HarmonyPrefix]
    private static void Prefix(CardTooltipController __instance) =>
        BilingualItemNameSubtitle.Hide(__instance);
}
