#nullable enable
using BazaarPlusPlus.Game.BilingualItemNames;
using BazaarPlusPlus.GameInterop.Fonts;
using BazaarPlusPlus.GameInterop.Localization;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Localization;
using HarmonyLib;
using TheBazaar.Extensions;
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
                return;

            var titleToken = tooltipData.CardTemplate.Localization?.Title;
            var secondaryTitle = currentLanguageIsChinese
                ? titleToken?.Text
                : ChineseTranslationCatalog.TryResolve(titleToken);
            var title = BilingualItemNamePresentation.TryBuild(
                controller.headerText?.text,
                secondaryTitle,
                enabled,
                isSupportedCard: true,
                alignEnglishSubtitle: currentLanguageIsChinese
            );
            if (title == null || controller.headerText == null)
                return;

            if (
                !currentLanguageIsChinese
                && NativeGameTypography.EnsureNativeTextCoverage(
                    controller.headerText,
                    secondaryTitle
                )
                    is not (
                        NativeGameTypography.Outcome.Applied
                        or NativeGameTypography.Outcome.NotNeeded
                    )
            )
                return;
            controller.headerText.TrySetText(title);
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
