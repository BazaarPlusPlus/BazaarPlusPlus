#nullable enable
using BazaarPlusPlus.Game.Tooltips;
using HarmonyLib;
using TheBazaar.Tooltips;
using TheBazaar.UI.Tooltips;

namespace BazaarPlusPlus.Patches.Tooltips;

[HarmonyPatch(typeof(CooldownRenderer), nameof(CooldownRenderer.RenderFromTooltip))]
internal static class UpgradePreviewCooldownPatch
{
    [HarmonyPostfix]
    private static void Postfix(CooldownRenderer __instance, CardTooltipData tooltipData)
    {
        if (
            !UpgradePreviewValueRegistry.TryResolveEffectiveCooldowns(
                tooltipData,
                out var currentSeconds,
                out var upgradedSeconds
            )
        )
            return;

        var currentText = $"{currentSeconds:F1}";
        var upgradedText = $"{upgradedSeconds:F1}";
        __instance.SetCooldown(
            currentText,
            canFuse: !string.Equals(currentText, upgradedText, StringComparison.Ordinal),
            upgradedText
        );
    }
}
