#pragma warning disable CS0436
using System.Text;
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.Game.Input;
using BazaarPlusPlus.Game.ItemEnchantPreview;
using HarmonyLib;
using TheBazaar;
using TheBazaar.Tooltips;
using TheBazaar.UI.Tooltips;

namespace BazaarPlusPlus;

// Item enchant preview: append BazaarPlusPlus-generated text into passive tooltip block
[HarmonyPatch(typeof(CardTooltipData), nameof(CardTooltipData.GetPassiveTooltipBlock))]
public static class CardTooltipDataPassivePatch
{
    private static void AppendTooltipText(StringBuilder builder, string text)
    {
        if (builder == null || string.IsNullOrWhiteSpace(text))
            return;

        var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n').TrimEnd('\n');
        if (normalized.Length == 0)
            return;

        var lines = normalized.Split('\n');
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            builder.Append(line);
            builder.Append('\n');
        }
    }

    [HarmonyPostfix]
    static void Postfix(
        CardTooltipData __instance,
        ref System.ValueTuple<StringBuilder, TooltipSegment?> __result
    )
    {
        try
        {
            if (__result.Item1 == null)
                return;

            if (Data.IsInCombat)
                return;

            if (BppHotkeyService.IsHeld(BppHotkeyActionId.HoldUpgradePreview))
                return;

            var alwaysShow = BppRuntimeHost.Config.EnchantPreviewAlwaysShowConfig?.Value ?? true;
            if (!alwaysShow && !BppHotkeyService.IsHeld(BppHotkeyActionId.HoldEnchantPreview))
                return;

            var previewSegments = ItemEnchantPreviewService.BuildPreviewSegments(
                __instance.CardInstance
            );
            if (previewSegments.Count == 0)
                return;

            var passiveBuilder = __result.Item1;
            if (passiveBuilder.Length > 0 && passiveBuilder[passiveBuilder.Length - 1] != '\n')
            {
                passiveBuilder.Append('\n');
            }

            passiveBuilder.Append("Bazaar++\n");

            foreach (var segment in previewSegments)
            {
                if (!string.IsNullOrWhiteSpace(segment.Text))
                    AppendTooltipText(passiveBuilder, segment.Text);
            }
        }
        catch (System.Exception ex)
        {
            BppLog.Error("ItemEnchantPreview", "Failed to append passive tooltip previews", ex);
        }
    }
}
