#nullable enable
using BazaarPlusPlus.GameInterop.Tooltips;
using HarmonyLib;
using TheBazaar;
using TheBazaar.UI.Tooltips;

namespace BazaarPlusPlus.Patches.Tooltips;

[HarmonyPatch(typeof(CardController), nameof(CardController.OnPointerEnter))]
internal static class NativeTooltipSuppressionCardPointerEnterPatch
{
    [HarmonyPrefix]
    private static bool Prefix() => !NativeTooltipSuppression.IsActive;
}

[HarmonyPatch(typeof(ItemController), nameof(ItemController.OnPointerMove))]
internal static class NativeTooltipSuppressionItemPointerMovePatch
{
    [HarmonyPrefix]
    private static bool Prefix() => !NativeTooltipSuppression.IsActive;
}

[HarmonyPatch(typeof(SkillProxyRenderer), nameof(SkillProxyRenderer.OnPointerEnter))]
internal static class NativeTooltipSuppressionSkillPointerEnterPatch
{
    [HarmonyPrefix]
    private static bool Prefix() => !NativeTooltipSuppression.IsActive;
}

[HarmonyPatch(typeof(RecapItemVisualController), nameof(RecapItemVisualController.OnPointerEnter))]
internal static class NativeTooltipSuppressionRecapItemPointerEnterPatch
{
    [HarmonyPrefix]
    private static bool Prefix() => !NativeTooltipSuppression.IsActive;
}

[HarmonyPatch(
    typeof(TooltipParentComponent),
    nameof(TooltipParentComponent.ShowCardTooltipController)
)]
internal static class NativeTooltipSuppressionCardTooltipRequestPatch
{
    [HarmonyPrefix]
    private static bool Prefix() => !NativeTooltipSuppression.IsActive;
}

[HarmonyPatch(
    typeof(TooltipParentComponent),
    nameof(TooltipParentComponent.ShowSecondaryCardTooltipController)
)]
internal static class NativeTooltipSuppressionSecondaryCardTooltipRequestPatch
{
    [HarmonyPrefix]
    private static bool Prefix(ref Task __result)
    {
        if (!NativeTooltipSuppression.IsActive)
            return true;

        __result = Task.CompletedTask;
        return false;
    }
}

[HarmonyPatch(
    typeof(TooltipParentComponent),
    nameof(TooltipParentComponent.ShowAuxiliaryTooltipController)
)]
internal static class NativeTooltipSuppressionAuxiliaryTooltipRequestPatch
{
    [HarmonyPrefix]
    private static bool Prefix() => !NativeTooltipSuppression.IsActive;
}

[HarmonyPatch(typeof(CardTooltipController), nameof(CardTooltipController.ShowTooltipController))]
internal static class NativeTooltipSuppressionCardTooltipLateShowPatch
{
    [HarmonyPrefix]
    private static bool Prefix() => !NativeTooltipSuppression.IsActive;
}

[HarmonyPatch(
    typeof(AuxiliaryTooltipController),
    nameof(AuxiliaryTooltipController.ShowAuxiliaryTooltipController)
)]
internal static class NativeTooltipSuppressionAuxiliaryTooltipLateShowPatch
{
    [HarmonyPrefix]
    private static bool Prefix() => !NativeTooltipSuppression.IsActive;
}
