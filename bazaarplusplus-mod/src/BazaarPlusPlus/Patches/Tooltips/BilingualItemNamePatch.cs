#nullable enable
using BazaarPlusPlus.Game.BilingualItemNames;
using BazaarPlusPlus.GameInterop.Localization;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using BazaarPlusPlus.Localization;
using HarmonyLib;
using TheBazaar.Tooltips;
using TheBazaar.UI.Tooltips;

namespace BazaarPlusPlus.Patches.Tooltips;

// Patch RenderCardUI before native positioning so the layout includes the subtitle.
[HarmonyPatch(typeof(CardTooltipTypeHandler), "RenderCardUI")]
internal static class BilingualItemNamePatch
{
    [HarmonyPostfix]
    private static void Postfix(CardTooltipController controller, CardTooltipData tooltipData)
    {
        try
        {
            var card = tooltipData.CardInstance;
            var enabled = BppPatchHost.Services.Config.EnableBilingualItemNamesConfig.Value;
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
                secondaryTitle
            );
            if (subtitle == null)
            {
                BilingualItemNameSubtitle.Hide(controller);
                return;
            }

            if (!BilingualItemNameSubtitle.TryShow(controller, subtitle, currentLanguageIsChinese))
            {
                BilingualItemNameSubtitle.Hide(controller);
                return;
            }
        }
        catch (Exception ex)
        {
            BppLog.WarnEvent(
                new BppLogEvent(
                    BppLogFeatureScope.BilingualItemNames,
                    "bilingual_item_names.tooltip.degraded",
                    storm: ["reason_code"]
                ),
                ex,
                ("reason_code", BilingualLogReasonCode.TooltipPatchException)
            );
        }
    }
}

// Hero-level and PvP tooltips bypass RenderCardUI but reuse its controller. Clear the
// subtitle at the shared reset seam to prevent the previous name from leaking.
[HarmonyPatch(typeof(CardTooltipController), nameof(CardTooltipController.ResetValues))]
internal static class BilingualItemNameResetPatch
{
    [HarmonyPrefix]
    private static void Prefix(CardTooltipController __instance) =>
        BilingualItemNameSubtitle.Hide(__instance);
}
