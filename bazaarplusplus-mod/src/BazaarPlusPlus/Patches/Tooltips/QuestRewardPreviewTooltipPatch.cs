#nullable enable
#pragma warning disable CS0436
using System.Reflection;
using System.Runtime.CompilerServices;
using BazaarGameShared.Domain.Tooltips;
using BazaarPlusPlus.Game.DetailedPreview;
using BazaarPlusPlus.Game.QuestPreview;
using BazaarPlusPlus.Game.Tooltips;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using HarmonyLib;
using TheBazaar;
using TheBazaar.Tooltips;
using TheBazaar.UI.Tooltips;
using UnityEngine;

namespace BazaarPlusPlus.Patches.Tooltips;

[HarmonyPatch(typeof(TooltipQuestEntry), nameof(TooltipQuestEntry.SetData))]
internal static class QuestRewardPreviewTooltipPatch
{
    private static readonly FieldInfo? DescriptionTextField = AccessTools.Field(
        typeof(TooltipQuestEntry),
        "_descriptionText"
    );
    private static readonly ConditionalWeakTable<
        TMP_Text,
        TextLayoutBaseline
    > DescriptionLayoutBaselines = new();
    private static readonly List<ActivePreviewPresentation> ActivePreviewPresentations = new();

    private static bool LayoutDirty;

    [HarmonyPostfix]
    private static void Postfix(
        TooltipQuestEntry __instance,
        CardQuestEntryData entry,
        CardTooltipData currentTooltipData
    )
    {
        try
        {
            // The native icon remains useful as a compact reward marker, but unlocked tooltips
            // cannot enter its nested hover. Keep the reward text inline on every tooltip surface.
            if (DescriptionTextField?.GetValue(__instance) is not TMP_Text descriptionText)
                return;

            if (!DetailedPreviewGate.IsEnabled())
            {
                RestoreNativeTextLayout(descriptionText);
                return;
            }

            var rewardTooltips = entry.QuestEntry.Reward?.Localization?.Tooltips;
            if (rewardTooltips == null)
            {
                RestoreNativeTextLayout(descriptionText);
                return;
            }

            // Native SetData unconditionally rendered the quest text into the description
            // field right before this postfix, so it is read back instead of paying a
            // second RenderQuestTooltips pass.
            var questText = descriptionText.text;
            var passiveRewardText = currentTooltipData.RenderQuestTooltips(
                rewardTooltips,
                ETooltipType.Passive
            );
            var activeRewardText = currentTooltipData.RenderQuestTooltips(
                rewardTooltips,
                ETooltipType.Active
            );
            var text = BppQuestRewardPreviewText.AppendRewardPreview(
                questText,
                passiveRewardText,
                activeRewardText
            );

            if (string.Equals(text, questText, StringComparison.Ordinal))
            {
                RestoreNativeTextLayout(descriptionText);
                return;
            }

            var hasInlineSprite = BppQuestRewardPreviewText.ContainsInlineSprite(
                questText,
                passiveRewardText,
                activeRewardText
            );
            // Before the first change, so a throw part way still rebuilds the group.
            LayoutDirty = true;
            ApplyPreviewTextLayout(descriptionText, hasInlineSprite);
            TrackPreviewPresentation(descriptionText, questText);
            descriptionText.text = text;
            descriptionText.ForceMeshUpdate();
        }
        catch (Exception ex)
        {
            ReportDegraded(ex);
        }
    }

    internal static bool ConsumeLayoutDirty()
    {
        var dirty = LayoutDirty;
        LayoutDirty = false;
        return dirty;
    }

    private static void ApplyPreviewTextLayout(TMP_Text descriptionText, bool hasInlineSprite)
    {
        var baseline = DescriptionLayoutBaselines.GetValue(
            descriptionText,
            text => new TextLayoutBaseline(text.margin, text.lineSpacing)
        );
        var nativeMargins = baseline.Margin;
        descriptionText.margin = new Vector4(
            nativeMargins.x,
            nativeMargins.y * BppQuestRewardPreviewText.VerticalMarginScale,
            nativeMargins.z,
            nativeMargins.w * BppQuestRewardPreviewText.VerticalMarginScale
        );
        descriptionText.lineSpacing = hasInlineSprite
            ? baseline.LineSpacing
            : baseline.LineSpacing + BppQuestRewardPreviewText.RewardLineSpacingIncrease;
    }

    private static void RestoreNativeTextLayout(TMP_Text descriptionText)
    {
        if (
            RestoreNativeTextLayoutCore(descriptionText)
            | ForgetPreviewPresentation(descriptionText)
        )
            LayoutDirty = true;
    }

    private static bool RestoreNativeTextLayoutCore(TMP_Text descriptionText)
    {
        var restored = false;
        if (
            DescriptionLayoutBaselines.TryGetValue(descriptionText, out var baseline)
            && descriptionText.margin != baseline.Margin
        )
        {
            descriptionText.margin = baseline.Margin;
            restored = true;
        }
        if (
            DescriptionLayoutBaselines.TryGetValue(descriptionText, out baseline)
            && !Mathf.Approximately(descriptionText.lineSpacing, baseline.LineSpacing)
        )
        {
            descriptionText.lineSpacing = baseline.LineSpacing;
            restored = true;
        }
        return restored;
    }

    private static void TrackPreviewPresentation(TMP_Text descriptionText, string nativeText)
    {
        for (var index = ActivePreviewPresentations.Count - 1; index >= 0; index--)
        {
            var presentation = ActivePreviewPresentations[index];
            if (!presentation.Description.TryGetTarget(out var trackedDescription))
            {
                ActivePreviewPresentations.RemoveAt(index);
                continue;
            }

            if (!ReferenceEquals(trackedDescription, descriptionText))
                continue;

            presentation.NativeText = nativeText;
            return;
        }

        ActivePreviewPresentations.Add(new ActivePreviewPresentation(descriptionText, nativeText));
    }

    private static bool ForgetPreviewPresentation(TMP_Text descriptionText)
    {
        var forgot = false;
        for (var index = ActivePreviewPresentations.Count - 1; index >= 0; index--)
        {
            if (
                !ActivePreviewPresentations[index]
                    .Description.TryGetTarget(out var trackedDescription)
            )
            {
                ActivePreviewPresentations.RemoveAt(index);
                continue;
            }
            if (!ReferenceEquals(trackedDescription, descriptionText))
                continue;
            ActivePreviewPresentations.RemoveAt(index);
            forgot = true;
        }
        return forgot;
    }

    internal static void ClearPooledPresentation()
    {
        foreach (var presentation in ActivePreviewPresentations)
        {
            if (
                !presentation.Description.TryGetTarget(out var descriptionText)
                || descriptionText == null
            )
                continue;

            descriptionText.text = presentation.NativeText;
            RestoreNativeTextLayoutCore(descriptionText);
            descriptionText.ForceMeshUpdate();
            descriptionText.GetComponentInParent<TooltipQuestGroup>()?.ForceRebuildLayout();
        }

        ActivePreviewPresentations.Clear();
    }

    private sealed class TextLayoutBaseline(Vector4 margin, float lineSpacing)
    {
        internal Vector4 Margin { get; } = margin;
        internal float LineSpacing { get; } = lineSpacing;
    }

    private sealed class ActivePreviewPresentation(TMP_Text description, string nativeText)
    {
        internal WeakReference<TMP_Text> Description { get; } = new(description);
        internal string NativeText { get; set; } = nativeText;
    }

    internal static void ReportDegraded(Exception exception) =>
        BppLog.WarnEvent(
            new BppLogEvent(
                BppLogFeatureScope.Tooltips,
                "tooltips.section.degraded",
                storm: ["section_id", "reason_code"]
            ),
            exception,
            ("section_id", TooltipSectionId.QuestRewardPreview),
            ("reason_code", TooltipLogReasonCode.RenderException)
        );
}

// QuestDisplayService only rebuilds the outer parent. Rebuild this group after SetData
// so the next outer pass sees corrected heights for rewritten or restored pooled entries.
[HarmonyPatch(typeof(TooltipQuestGroup), nameof(TooltipQuestGroup.SetData))]
internal static class QuestRewardPreviewQuestGroupLayoutPatch
{
    [HarmonyPostfix]
    private static void Postfix(TooltipQuestGroup __instance)
    {
        try
        {
            if (QuestRewardPreviewTooltipPatch.ConsumeLayoutDirty())
                __instance.ForceRebuildLayout();
        }
        catch (Exception ex)
        {
            QuestRewardPreviewTooltipPatch.ReportDegraded(ex);
        }
    }
}
