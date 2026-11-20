using BazaarPlusPlus.Android.GameInterop;
using BazaarPlusPlus.Core.Cards;
using BazaarPlusPlus.Game.EncounterPreview;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using BazaarPlusPlus.Mobile.Previews;
using HarmonyLib;
using Il2CppBazaarGameShared.Domain.Core.Types;
using Il2CppTheBazaar.Tooltips;
using Il2CppTheBazaar.UI.Tooltips;
using UnityEngine;

namespace BazaarPlusPlus.Android.Patches;

// RenderPassiveEffectTextBlock would run EncounterPreviewPatch against the pooled controller's
// previous card.
[HarmonyPatch(
    typeof(HeroLevelTooltipTypeHandler),
    nameof(HeroLevelTooltipTypeHandler.HandleTooltip)
)]
internal static class HeroLevelRewardsTooltipPatch
{
    [HarmonyPostfix]
    private static void Postfix(
        CardTooltipController controller,
        Transform parent,
        Vector3 offset,
        ITooltipData tooltipData,
        bool parentInScreenSpace
    )
    {
        try
        {
            var preview = AndroidPatchHost.Features?.EncounterPreview?.Module;
            if (preview?.Enabled != true)
                return;

            var heroLevel = tooltipData?.TryCast<HeroLevelTooltipData>();
            if (heroLevel == null)
            {
                ReportObserved("skipped", "not_hero_level_data", level: 0, contentLength: 0);
                return;
            }

            // The pooled tooltip's reset leaves quest rows from the previous card behind.
            controller._questDisplayService?.BuildDisplay(null, null);

            // GetCurrentAndNextLevel returns a tuple that does not marshal through IL2CPP interop.
            var player = heroLevel._player;
            var level =
                player?.Attributes?.TryGetValue(EPlayerAttributeType.Level, out var value) == true
                    ? value
                    : 0;
            CardHero? hero =
                player != null
                && AndroidCardFacets.Hero.TryToFacet(player.Hero, out var facet)
                && facet != CardHero.Common
                    ? facet
                    : null;
            var result = preview.ResolveLevelUp(new LevelUpPreviewQuery(level, hero));
            var content =
                result.Availability == EncounterPreviewAvailability.Available
                    ? result.Content
                    : null;
            if (string.IsNullOrWhiteSpace(content))
            {
                ReportObserved("skipped", "no_content", level, contentLength: 0);
                return;
            }

            var text = controller.passiveEffectText;
            var visibility = controller.passiveEffectParentVisibilityControl;
            var block = controller.passiveEffectParent?.transform;
            var strip = controller._heroLevelTooltipViewComponent?._parent?.transform;
            if (
                text == null
                || visibility == null
                || block == null
                || strip == null
                || strip.parent != block.parent
            )
            {
                ReportObserved("section_unavailable", "section_unavailable", level, content.Length);
                return;
            }
            // Only this tooltip shows the reward strip, so reordering it is safe for other
            // tooltips.
            if (strip.GetSiblingIndex() > block.GetSiblingIndex())
                strip.SetSiblingIndex(block.GetSiblingIndex());
            visibility.SetVisible(true);
            controller.dividerParent?.SetActive(true);
            text.SetText(
                TouchPreviewTextSize.Wrap(
                    content,
                    EncounterPreviewPatch.PreviewTextPercent(AndroidPatchHost.Features)
                )
            );
            ReportObserved("rendered", "rendered", level, content.Length);

            // The handler positioned the tooltip before the text made it taller.
            controller.PositionTooltip(parent, offset, parentInScreenSpace);
        }
        catch (Exception exception)
        {
            BppLog.WarnEvent(
                new BppLogEvent(
                    BppLogFeatureScope.Tooltips,
                    "tooltips.level_rewards.degraded",
                    storm: ["reason_code"]
                ),
                exception,
                ("outcome", "failed"),
                ("reason_code", "render_exception"),
                ("level", 0),
                ("content_length", 0)
            );
        }
    }

    private static void ReportObserved(
        string outcome,
        string reasonCode,
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
