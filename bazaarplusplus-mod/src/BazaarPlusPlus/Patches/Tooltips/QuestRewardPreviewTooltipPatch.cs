#nullable enable
#pragma warning disable CS0436
using System.Reflection;
using System.Runtime.CompilerServices;
using BazaarGameShared.Domain.Tooltips;
using BazaarPlusPlus.Game.ItemEnchantPreview;
using BazaarPlusPlus.Game.QuestPreview;
using BazaarPlusPlus.Game.Tooltips;
using BazaarPlusPlus.Infrastructure;
using HarmonyLib;
using TheBazaar;
using TheBazaar.Tooltips;
using TheBazaar.UI.Tooltips;
using TMPro;
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

            if (!QuestPreviewGate.IsEnabled())
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
        RestoreNativeTextLayoutCore(descriptionText);
        ForgetPreviewPresentation(descriptionText);
    }

    private static void RestoreNativeTextLayoutCore(TMP_Text descriptionText)
    {
        if (
            DescriptionLayoutBaselines.TryGetValue(descriptionText, out var baseline)
            && descriptionText.margin != baseline.Margin
        )
            descriptionText.margin = baseline.Margin;
        if (
            DescriptionLayoutBaselines.TryGetValue(descriptionText, out baseline)
            && !Mathf.Approximately(descriptionText.lineSpacing, baseline.LineSpacing)
        )
            descriptionText.lineSpacing = baseline.LineSpacing;
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

    private static void ForgetPreviewPresentation(TMP_Text descriptionText)
    {
        for (var index = ActivePreviewPresentations.Count - 1; index >= 0; index--)
        {
            if (
                !ActivePreviewPresentations[index]
                    .Description.TryGetTarget(out var trackedDescription)
                || ReferenceEquals(trackedDescription, descriptionText)
            )
                ActivePreviewPresentations.RemoveAt(index);
        }
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
            TooltipLogEvents.SectionDegraded,
            exception,
            TooltipLogEvents.SectionDegradedSectionId.Bind(TooltipSectionId.QuestRewardPreview),
            TooltipLogEvents.SectionDegradedReasonCode.Bind(TooltipLogReasonCode.RenderException)
        );
}

// QuestDisplayService only rebuilds the outer quest-group parent after populating its rows.
// Our entry postfix applies the preview layout or restores the native baseline after each native
// SetData call, so pooled entries can retain the previous card's height in either transition.
// Rebuild the group once all entries have been populated; the native outer-parent rebuild that
// follows then consumes the corrected sizes.
[HarmonyPatch(typeof(TooltipQuestGroup), nameof(TooltipQuestGroup.SetData))]
internal static class QuestRewardPreviewQuestGroupLayoutPatch
{
    [HarmonyPostfix]
    private static void Postfix(TooltipQuestGroup __instance)
    {
        try
        {
            __instance.ForceRebuildLayout();
        }
        catch (Exception ex)
        {
            QuestRewardPreviewTooltipPatch.ReportDegraded(ex);
        }
    }
}

internal static class BppQuestRewardPreviewText
{
    private const string SpriteMarkupPrefix = "<sprite";
    private const int RewardSizePercent = 76;
    internal const float VerticalMarginScale = 0.5f;
    internal const float RewardLineSpacingIncrease = 12f;
    private const float RewardInlineSizeScale = RewardSizePercent / 100f;

    public static string AppendRewardPreview(
        string? questText,
        string? passiveRewardText,
        string? activeRewardText
    )
    {
        var normalizedQuestText = (questText ?? string.Empty).Trim();
        if (normalizedQuestText.Length == 0)
            return normalizedQuestText;

        var rewardText = BuildRewardText(passiveRewardText, activeRewardText);
        if (rewardText.Length == 0)
            return normalizedQuestText;

        return $"{normalizedQuestText}\n<size={RewardSizePercent}%>{rewardText}</size>";
    }

    internal static bool ContainsInlineSprite(params string?[] values)
    {
        foreach (var value in values)
        {
            if (value?.IndexOf(SpriteMarkupPrefix, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        }

        return false;
    }

    private static string BuildRewardText(params string?[] values)
    {
        var lines = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in values)
        {
            var line = ItemEnchantPreviewFormatting.ScaleInlineSizes(
                NormalizeInline(value),
                RewardInlineSizeScale
            );
            if (line.Length == 0 || !seen.Add(line))
                continue;
            lines.Add(line);
        }

        return string.Join(" / ", lines);
    }

    private static string NormalizeInline(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        return string.Join(
            " ",
            text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim())
                .Where(line => line.Length > 0)
        );
    }
}
