#nullable enable
#pragma warning disable CS0436
using BazaarPlusPlus.Core.GameState;
using BazaarPlusPlus.Game.ItemEnchantPreview;
using BazaarPlusPlus.Game.Tooltips;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using HarmonyLib;
using TheBazaar;
using TheBazaar.Tooltips;
using TheBazaar.UI.Tooltips;

namespace BazaarPlusPlus.Patches.Tooltips;

// Renders the enchant preview beneath the native passive-effect block.
[HarmonyPatch(
    typeof(CardTooltipController),
    nameof(CardTooltipController.RenderPassiveEffectTextBlock)
)]
internal static class BppTooltipSectionRenderPatch
{
    internal const string EnchantWithNativeSectionKey = "enchant-preview-with-native";
    internal const string EnchantAfterQuestSectionKey = "enchant-preview-after-quest";
    internal const string EnchantWithoutNativeSectionKey = "enchant-preview-without-native";

    private static readonly BppTooltipSections.Style EnchantWithNativeStyle =
        CreateEnchantWithNativeStyle(sectionTopPaddingScale: 1f, sourceBottomPaddingScale: 0.5f);

    private static readonly BppTooltipSections.Style EnchantAfterQuestStyle =
        CreateEnchantWithNativeStyle(
            sectionTopPaddingScale: 0f,
            sourceBottomPaddingScale: 0f,
            questGroupBottomPaddingScale: 0f
        );

    private static readonly BppTooltipSections.Style EnchantWithoutNativeStyle = new()
    {
        SectionTopPaddingScale = 1.25f,
        SectionBottomPaddingScale = 1.75f,
        ParagraphSpacing = 4f,
        FontScale = 1.2f,
    };
    private static readonly OperationalHealthTracker<
        ItemEnchantEncounterProbe,
        EncounterProbeFailureReason
    > EncounterHealth = new();

    [HarmonyPostfix]
    [HarmonyPriority(Priority.Last)]
    private static void Postfix(
        CardTooltipController __instance,
        string text,
        List<CardQuestGroupData>? questData
    )
    {
        try
        {
            ItemEnchantPreviewTooltipLifecycle.Hide(__instance);

            // ResetValues is the only native caller that passes null quest data.
            // Bail out before reading CurrentTooltipData, which is still stale then.
            if (questData == null)
                return;

            var enchantContent = BuildEnchantContent(__instance);
            if (string.IsNullOrEmpty(enchantContent))
                return;

            var (sectionKey, sectionStyle) = ResolveSectionLayout(text, questData.Count);

            if (
                BppTooltipSections.TryShow(
                    __instance,
                    sectionKey,
                    __instance.passiveEffectParent,
                    enchantContent!,
                    sectionStyle
                )
            )
                TooltipLayerOverride.SetElevated(__instance, elevated: true);
        }
        catch (Exception ex)
        {
            ReportSectionDegraded(TooltipSectionId.EnchantPreview, ex);
        }
    }

    internal static bool HasNativeContent(string passiveText, int questGroupCount) =>
        !string.IsNullOrWhiteSpace(passiveText) || questGroupCount > 0;

    internal static (string SectionKey, BppTooltipSections.Style Style) ResolveSectionLayout(
        string passiveText,
        int questGroupCount
    )
    {
        if (!string.IsNullOrWhiteSpace(passiveText))
            return (EnchantWithNativeSectionKey, EnchantWithNativeStyle);
        if (questGroupCount > 0)
            return (EnchantAfterQuestSectionKey, EnchantAfterQuestStyle);
        return (EnchantWithoutNativeSectionKey, EnchantWithoutNativeStyle);
    }

    private static BppTooltipSections.Style CreateEnchantWithNativeStyle(
        float sectionTopPaddingScale,
        float sourceBottomPaddingScale,
        float? questGroupBottomPaddingScale = null
    ) =>
        new()
        {
            SectionTopPaddingScale = sectionTopPaddingScale,
            SectionBottomPaddingScale = 1.75f,
            SourceBottomPaddingScale = sourceBottomPaddingScale,
            QuestGroupBottomPaddingScale = questGroupBottomPaddingScale,
            ParagraphSpacing = 4f,
            FontScale = 1.2f,
            ShowNativeDivider = true,
            DividerHorizontalInset = 12f,
        };

    internal static void ResetEncounterHealth() => EncounterHealth.Reset();

    private static void ReportSectionDegraded(TooltipSectionId sectionId, Exception exception) =>
        BppLog.WarnEvent(
            TooltipLogEvents.SectionDegraded,
            exception,
            TooltipLogEvents.SectionDegradedSectionId.Bind(sectionId),
            TooltipLogEvents.SectionDegradedReasonCode.Bind(TooltipLogReasonCode.RenderException)
        );

    private static string? BuildEnchantContent(CardTooltipController controller)
    {
        if (Data.IsInCombat || controller.CurrentTooltipData is not CardTooltipData tooltipData)
            return null;

        var services = BppPatchHost.Services;
        ChoicePedestalSnapshot? choicePedestal = TooltipPreviewModePolicy.ShouldReadChoicePedestal(
            services.Config
        )
            ? ReadChoicePedestal(services.EncounterState)
            : null;
        if (
            TooltipPreviewModePolicy.Resolve(services.Config, choicePedestal)
            != TooltipPreviewMode.Enchant
        )
            return null;

        var restrictTo = TooltipPreviewModePolicy.ResolveEnchantRestriction(
            services.Config,
            choicePedestal
        );
        var segments = ItemEnchantPreviewService.BuildPreviewSegments(
            tooltipData.CardInstance,
            restrictTo
        );
        return ItemEnchantPreviewFormatting.BuildSectionText(segments);
    }

    private static ChoicePedestalSnapshot? ReadChoicePedestal(IEncounterStateProbe? probe)
    {
        if (probe == null)
            return null;
        if (probe is not ITypedEncounterStateProbe typed)
        {
            ReportEncounterSuccess();
            return probe.GetChoicePedestal();
        }

        var outcome = typed.GetChoicePedestalOutcome();
        if (outcome.IsSuccess)
        {
            ReportEncounterSuccess();
            return outcome.Snapshot;
        }

        if (
            EncounterHealth.ObserveFailure(
                ItemEnchantEncounterProbe.Encounter,
                outcome.FailureReason
            )
        )
        {
            var fields = new[]
            {
                ItemEnchantPreviewLogEvents.EncounterProbeDegradedProbe.Bind(
                    ItemEnchantEncounterProbe.Encounter
                ),
                ItemEnchantPreviewLogEvents.EncounterProbeDegradedReasonCode.Bind(
                    outcome.FailureReason
                ),
            };
            if (outcome.Exception == null)
                BppLog.WarnEvent(ItemEnchantPreviewLogEvents.EncounterProbeDegraded, fields);
            else
                BppLog.WarnEvent(
                    ItemEnchantPreviewLogEvents.EncounterProbeDegraded,
                    outcome.Exception,
                    fields
                );
        }
        return outcome.Snapshot;
    }

    private static void ReportEncounterSuccess()
    {
        if (
            !EncounterHealth.ObserveSuccess(ItemEnchantEncounterProbe.Encounter, out var reasonCode)
        )
            return;
        BppLog.RecoverStorm(
            ItemEnchantPreviewLogEvents.EncounterProbeDegraded,
            ItemEnchantPreviewLogEvents.EncounterProbeDegradedProbe.Bind(
                ItemEnchantEncounterProbe.Encounter
            ),
            ItemEnchantPreviewLogEvents.EncounterProbeDegradedReasonCode.Bind(reasonCode)
        );
        BppLog.InfoEvent(
            ItemEnchantPreviewLogEvents.EncounterProbeRecovered,
            ItemEnchantPreviewLogEvents.EncounterProbeRecoveredProbe.Bind(
                ItemEnchantEncounterProbe.Encounter
            )
        );
    }
}

[HarmonyPatch(typeof(CardTooltipController), nameof(CardTooltipController.ResetValues))]
internal static class BppTooltipSectionResetPatch
{
    [HarmonyPrefix]
    private static void Prefix(CardTooltipController __instance) =>
        ItemEnchantPreviewTooltipLifecycle.Hide(__instance);

    [HarmonyFinalizer]
    private static void Finalizer(CardTooltipController __instance) =>
        ItemEnchantPreviewTooltipLifecycle.Hide(__instance);
}

[HarmonyPatch(typeof(CardTooltipController), nameof(CardTooltipController.ClearCurrentCard))]
internal static class BppTooltipSectionClearPatch
{
    [HarmonyPrefix]
    private static void Prefix(CardTooltipController __instance) =>
        ItemEnchantPreviewTooltipLifecycle.Hide(__instance);
}

[HarmonyPatch(typeof(CardTooltipController), "OnDisable")]
internal static class BppTooltipSectionDisablePatch
{
    [HarmonyPrefix]
    private static void Prefix(CardTooltipController __instance) =>
        ItemEnchantPreviewTooltipLifecycle.Hide(__instance);
}

[HarmonyPatch(typeof(CardTooltipController), nameof(CardTooltipController.OnDestroy))]
internal static class BppTooltipSectionDestroyPatch
{
    [HarmonyPrefix]
    private static void Prefix(CardTooltipController __instance)
    {
        TooltipLayerOverride.SetElevated(__instance, elevated: false);
        BppTooltipSections.ReleaseAll(__instance);
    }
}

internal static class ItemEnchantPreviewTooltipLifecycle
{
    internal static void Hide(CardTooltipController controller)
    {
        BppTooltipSections.Hide(
            controller,
            BppTooltipSectionRenderPatch.EnchantWithNativeSectionKey
        );
        BppTooltipSections.Hide(
            controller,
            BppTooltipSectionRenderPatch.EnchantAfterQuestSectionKey
        );
        BppTooltipSections.Hide(
            controller,
            BppTooltipSectionRenderPatch.EnchantWithoutNativeSectionKey
        );
        TooltipLayerOverride.SetElevated(controller, elevated: false);
    }
}
