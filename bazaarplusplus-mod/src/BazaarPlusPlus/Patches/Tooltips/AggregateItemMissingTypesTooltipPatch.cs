#nullable enable
#pragma warning disable CS0436
using BazaarGameClient.Domain.Models.Cards;
using BazaarPlusPlus.Game.QuestPreview;
using BazaarPlusPlus.Game.Tooltips;
using BazaarPlusPlus.GameInterop.LiveCards;
using BazaarPlusPlus.GameInterop.StaticCards;
using BazaarPlusPlus.Infrastructure;
using HarmonyLib;
using TheBazaar.UI.Tooltips;

namespace BazaarPlusPlus.Patches.Tooltips;

[HarmonyPatch(
    typeof(CardTooltipController),
    nameof(CardTooltipController.RenderPassiveEffectTextBlock)
)]
internal static class AggregateItemMissingTypesTooltipPatch
{
    private const string SectionKey = "aggregate-missing-types";

    private static readonly BppTooltipSections.Style SectionStyle = new() { FontScale = 0.65f };

    [HarmonyPostfix]
    private static void Postfix(CardTooltipController __instance, string text)
    {
        try
        {
            BppTooltipSections.Hide(__instance, SectionKey);
            if (!QuestPreviewGate.IsEnabled())
                return;

            var content = string.IsNullOrEmpty(text) ? null : BuildContent(__instance);
            if (string.IsNullOrEmpty(content))
                return;

            if (
                !BppTooltipSections.TryShow(
                    __instance,
                    SectionKey,
                    __instance.passiveEffectParent,
                    content!,
                    SectionStyle
                )
            )
                BppTooltipSections.Hide(__instance, SectionKey);
        }
        catch (Exception ex)
        {
            BppLog.WarnEvent(
                TooltipLogEvents.SectionDegraded,
                ex,
                TooltipLogEvents.SectionDegradedSectionId.Bind(
                    TooltipSectionId.AggregateMissingTypes
                ),
                TooltipLogEvents.SectionDegradedReasonCode.Bind(
                    TooltipLogReasonCode.RenderException
                )
            );
        }
    }

    internal static void ClearPooledPresentation() => BppTooltipSections.HideAll(SectionKey);

    private static string? BuildContent(CardTooltipController controller)
    {
        var card = controller._currentCard;
        if (card == null)
            return null;

        var staticData = BppStaticDataAccess.TryGetReadyManagerObject();
        var template = BppStaticDataAccess.GetCardTemplate(staticData, card.TemplateId);
        var activeEnchantment = (card as ItemCard)?.Enchantment;
        if (
            template == null
            || !AggregateItemTypeSourceResolver.TryResolve(
                template,
                activeEnchantment,
                out var source
            )
        )
            return null;

        var present =
            source.Section == null
                ? card.Tags
                : LiveCardTagReader.TryReadSectionTypes(
                    source.Section.Value,
                    source.ExcludeSelf ? card : null
                );
        return present == null
            ? null
            : AggregateItemMissingTypesText.Build(
                present,
                BppTooltipText.ColorKeywords,
                BppTooltipText.TryLocalizeKeyword
            );
    }
}
