#nullable enable
#pragma warning disable CS0436, Harmony003
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Game.EventPreview;
using BazaarPlusPlus.Game.Tooltips;
using BazaarPlusPlus.Infrastructure;
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
            var content =
                string.IsNullOrEmpty(text) || !EventPreviewGate.IsEnabled()
                    ? null
                    : ResolveContent(__instance, text);
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
                TooltipLogEvents.EncounterSectionDegraded,
                ex,
                TooltipLogEvents.EncounterSectionReasonCode.Bind(
                    TooltipLogReasonCode.RenderException
                )
            );
        }
    }

    private static string? ResolveContent(CardTooltipController controller, string nativeText)
    {
        var card = controller._currentCard;
        if (card == null)
            return null;

        var module = BppPatchHost.Features.EncounterPreview;
        return card.Type switch
        {
            ECardType.EventEncounter => Available(
                module.ResolveEvent(new EventPreviewQuery(card.TemplateId, nativeText))
            ),
            ECardType.EncounterStep => Available(
                module.ResolveStep(new EncounterStepPreviewQuery(card.TemplateId, nativeText))
            ),
            _ => null,
        };
    }

    private static string? Available(EncounterPreviewResult preview) =>
        preview.Availability == EventPreviewAvailability.Available ? preview.Content : null;

    private static string? Available(EncounterStepPreviewResult preview) =>
        preview.Availability == EventPreviewAvailability.Available ? preview.Content : null;
}
