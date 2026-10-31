using BazaarPlusPlus.Android.GameInterop;
using BazaarPlusPlus.Core.Cards;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using HarmonyLib;
using Il2CppBazaarGameShared.Domain.Core.Types;
using Il2CppTheBazaar.Tooltips;
using Il2CppTheBazaar.UI.Tooltips;
using UnityEngine;

namespace BazaarPlusPlus.Android.Patches;

/// <summary>
/// Writes the level-up rewards (<see cref="Game.AndroidEncounterPreview.RenderLevelUp"/>) into
/// the hero-level tooltip's passive block, below its "next level rewards" icon strip, as the
/// desktop's <c>HeroLevelRewardsTooltipPatch</c> appends its section. The handler starts with
/// <c>ResetValues</c>, which empties that block, so a switched-off preview leaves the native
/// tooltip. The text goes to the block directly: <c>RenderPassiveEffectTextBlock</c> would also
/// run <see cref="EncounterPreviewPatch"/> against the pooled controller's last card.
/// </summary>
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
            var preview = AndroidPatchHost.EncounterPreview;
            if (preview?.Enabled != true)
                return;

            var heroLevel = tooltipData?.TryCast<HeroLevelTooltipData>();
            if (heroLevel == null)
            {
                ReportObserved("skipped", "not_hero_level_data", level: 0, contentLength: 0);
                return;
            }

            // As on the desktop: a pooled controller that last showed a quest item keeps its
            // quest rows, which the handler's reset does not clear.
            controller._questDisplayService?.BuildDisplay(null, null);

            // The level as GetCurrentAndNextLevel reads it, whose returned tuple does not marshal
            // through the interop. The badge's own player, so a replay shows the rewards of the
            // hero it replays.
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
            var content = preview.RenderLevelUp(level, hero);
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
            // The prefab lays the passive block out above the strip; the desktop's section sits
            // below it. Only this tooltip shows the strip, so moving it right before the block
            // leaves every other tooltip's order as it was.
            if (strip.GetSiblingIndex() > block.GetSiblingIndex())
                strip.SetSiblingIndex(block.GetSiblingIndex());
            visibility.SetVisible(true);
            controller.dividerParent?.SetActive(true);
            text.SetText("<size=75%>" + content + "</size>");
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

    // The desktop's event and fields; the values are its outcome and reason enums' log names.
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
