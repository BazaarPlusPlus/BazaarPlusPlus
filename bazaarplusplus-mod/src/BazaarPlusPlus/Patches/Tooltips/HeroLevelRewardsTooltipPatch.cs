#nullable enable
#pragma warning disable CS0436
using BazaarPlusPlus.Game.EventPreview;
using BazaarPlusPlus.Game.Tooltips;
using BazaarPlusPlus.Infrastructure;
using HarmonyLib;
using TheBazaar.Tooltips;
using TheBazaar.UI.Tooltips;
using UnityEngine;

namespace BazaarPlusPlus.Patches.Tooltips;

// Appends a readable reward breakdown below the native hero-level tooltip
// ("NEXT LEVEL REWARDS" icon strip). HandleTooltip starts with ResetValues, which
// routes through RenderPassiveEffectTextBlock(empty) and hides all BPP sections, so
// showing the level section here needs no extra teardown elsewhere.
[HarmonyPatch(
    typeof(HeroLevelTooltipTypeHandler),
    nameof(HeroLevelTooltipTypeHandler.HandleTooltip)
)]
internal static class HeroLevelRewardsTooltipPatch
{
    internal const string SectionKey = "level-rewards";
    private static readonly BppTooltipSections.Style SectionStyle = new()
    {
        ParagraphSpacing = 0f,
        SectionTopPaddingScale = 1f,
    };

    [HarmonyPostfix]
    private static void Postfix(
        CardTooltipController controller,
        Transform parent,
        Vector3 offset,
        ITooltipData tooltipData
    )
    {
        try
        {
            if (!EventPreviewGate.IsEnabled())
                return;

            if (tooltipData is not HeroLevelTooltipData heroLevelTooltipData)
            {
                ReportObserved(
                    TooltipLevelRewardsOutcome.Skipped,
                    TooltipLogReasonCode.NotHeroLevelData,
                    level: 0,
                    contentLength: 0
                );
                return;
            }

            // Native gap: HandleTooltip resets the view via ResetValues, which clears
            // every section except the quest rows — a pooled controller that last
            // showed a quest item (e.g. a shop basket) leaks its "Sell N Food" rows
            // into the hero-level tooltip. Clear them the way the card path does.
            controller._questDisplayService?.BuildDisplay(null, null);

            var currentLevel = heroLevelTooltipData.GetCurrentAndNextLevel().currentLevel;
            var result = BppPatchHost.Features.EncounterPreview.ResolveLevelUp(
                new LevelUpPreviewQuery(currentLevel)
            );
            var content =
                result.Availability == EventPreviewAvailability.Available ? result.Content : null;
            if (string.IsNullOrEmpty(content))
            {
                ReportObserved(
                    TooltipLevelRewardsOutcome.Skipped,
                    TooltipLogReasonCode.NoContent,
                    currentLevel,
                    contentLength: 0
                );
                return;
            }

            var anchor = controller._heroLevelTooltipViewComponent?._parent;
            var shown = BppTooltipSections.TryShow(
                controller,
                SectionKey,
                anchor,
                content,
                SectionStyle
            );
            ReportObserved(
                shown
                    ? TooltipLevelRewardsOutcome.Rendered
                    : TooltipLevelRewardsOutcome.SectionUnavailable,
                shown ? TooltipLogReasonCode.Rendered : TooltipLogReasonCode.SectionUnavailable,
                currentLevel,
                content.Length
            );
            if (!shown)
                return;

            // The native handler positioned the tooltip before this section existed.
            controller.PositionTooltip(parent, offset);
        }
        catch (Exception ex)
        {
            BppLog.WarnEvent(
                TooltipLogEvents.LevelRewardsDegraded,
                ex,
                TooltipLogEvents.LevelRewardsOutcome.Bind(TooltipLevelRewardsOutcome.Failed),
                TooltipLogEvents.LevelRewardsReasonCode.Bind(TooltipLogReasonCode.RenderException),
                TooltipLogEvents.LevelRewardsLevel.Bind(0),
                TooltipLogEvents.LevelRewardsContentLength.Bind(0)
            );
        }
    }

    private static void ReportObserved(
        TooltipLevelRewardsOutcome outcome,
        TooltipLogReasonCode reasonCode,
        int level,
        int contentLength
    ) =>
        BppLog.DebugEvent(
            TooltipLogEvents.LevelRewardsRenderedOrSkipped,
            () =>
                [
                    TooltipLogEvents.LevelRewardsOutcome.Bind(outcome),
                    TooltipLogEvents.LevelRewardsReasonCode.Bind(reasonCode),
                    TooltipLogEvents.LevelRewardsLevel.Bind(level),
                    TooltipLogEvents.LevelRewardsContentLength.Bind(contentLength),
                ]
        );
}
