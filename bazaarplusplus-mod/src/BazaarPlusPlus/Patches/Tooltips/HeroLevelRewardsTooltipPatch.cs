#nullable enable
#pragma warning disable CS0436
using BazaarPlusPlus.Game.EncounterPreview;
using BazaarPlusPlus.Game.Tooltips;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using HarmonyLib;
using TheBazaar.Tooltips;
using TheBazaar.UI.Tooltips;
using UnityEngine;

namespace BazaarPlusPlus.Patches.Tooltips;

// HandleTooltip clears BPP sections via ResetValues, so the level section needs no extra teardown.
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

    internal static void ClearPooledPresentation() => BppTooltipSections.HideAll(SectionKey);

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
            var preview = BppPatchHost.Features.EncounterPreview;
            if (!preview.Enabled)
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

            // ResetValues leaves quest rows on pooled controllers. Clear them so the previous item
            // cannot leak into a hero-level tooltip.
            controller._questDisplayService?.BuildDisplay(null, null);

            var currentLevel = heroLevelTooltipData.GetCurrentAndNextLevel().currentLevel;
            var result = preview.ResolveLevelUp(new LevelUpPreviewQuery(currentLevel));
            var content =
                result.Availability == EncounterPreviewAvailability.Available
                    ? result.Content
                    : null;
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
                new BppLogEvent(
                    BppLogFeatureScope.Tooltips,
                    "tooltips.level_rewards.degraded",
                    storm: ["reason_code"]
                ),
                ex,
                ("outcome", TooltipLevelRewardsOutcome.Failed),
                ("reason_code", TooltipLogReasonCode.RenderException),
                ("level", 0),
                ("content_length", 0)
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
            new BppLogEvent(
                BppLogFeatureScope.Tooltips,
                "tooltips.level_rewards.rendered_or_skipped"
            ),
            () =>
                [
                    ("outcome", outcome),
                    ("reason_code", reasonCode),
                    ("level", level),
                    ("content_length", contentLength),
                ]
        );
}
