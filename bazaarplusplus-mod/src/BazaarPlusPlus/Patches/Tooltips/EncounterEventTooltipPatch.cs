#nullable enable
#pragma warning disable CS0436, Harmony003
using BazaarPlusPlus.Game.EncounterPreview;
using BazaarPlusPlus.Game.Tooltips;
using BazaarPlusPlus.GameInterop.Cards;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using HarmonyLib;
using TheBazaar.UI.Tooltips;

namespace BazaarPlusPlus.Patches.Tooltips;

[HarmonyPatch(
    typeof(CardTooltipController),
    nameof(CardTooltipController.RenderPassiveEffectTextBlock)
)]
internal static class EncounterEventTooltipPatch
{
    private const string SectionKey = "encounter";
    private static readonly BppTooltipSections.Style SectionStyle = new()
    {
        ParagraphSpacing = 0f,
        SourceBottomPaddingScale = 0.6f,
        NativeSectionBottomPaddingScale = 1.2f,
    };

    [HarmonyPostfix]
    private static void Postfix(CardTooltipController __instance, string text)
    {
        try
        {
            var content = ResolveContent(__instance, text);
            if (string.IsNullOrEmpty(content))
            {
                BppTooltipSections.Hide(__instance, SectionKey);
                BppTooltipSections.Hide(__instance, HeroLevelRewardsTooltipPatch.SectionKey);
                return;
            }

            if (
                !BppTooltipSections.TryShow(
                    __instance,
                    SectionKey,
                    __instance.passiveEffectParent,
                    content!,
                    SectionStyle
                )
            )
                return;
            BppTooltipSections.Hide(__instance, HeroLevelRewardsTooltipPatch.SectionKey);
        }
        catch (Exception ex)
        {
            BppLog.WarnEvent(
                new BppLogEvent(
                    BppLogFeatureScope.Tooltips,
                    "tooltips.encounter_section.degraded",
                    storm: ["reason_code"]
                ),
                ex,
                ("reason_code", TooltipLogReasonCode.RenderException)
            );
        }
    }

    internal static void ClearPooledPresentation() => BppTooltipSections.HideAll(SectionKey);

    private static string? ResolveContent(CardTooltipController controller, string nativeText)
    {
        var card = controller._currentCard;
        if (card == null || !DesktopCardFacets.Type.TryToFacet(card.Type, out var type))
            return null;

        var preview = BppPatchHost.Features.EncounterPreview.ResolveCard(
            new EncounterCardPreviewQuery(card.TemplateId, type, nativeText)
        );
        return preview.Availability == EncounterPreviewAvailability.Available
            ? preview.Content
            : null;
    }
}
