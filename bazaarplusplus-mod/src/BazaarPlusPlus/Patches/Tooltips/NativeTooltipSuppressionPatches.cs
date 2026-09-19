#nullable enable
using System.Reflection;
using BazaarPlusPlus.GameInterop.Tooltips;
using HarmonyLib;
using TheBazaar;
using TheBazaar.UI.Tooltips;

namespace BazaarPlusPlus.Patches.Tooltips;

[HarmonyPatch]
internal static class NativeTooltipSuppressionCardControllerAwakePatch
{
    private static MethodBase TargetMethod() =>
        AccessTools.DeclaredMethod(typeof(CardTooltipController), "Awake")
        ?? throw new MissingMethodException(typeof(CardTooltipController).FullName, "Awake");

    [HarmonyPostfix]
    private static void Postfix() => NativeTooltipSuppression.NotifyControllerLifecycleChanged();
}

[HarmonyPatch]
internal static class NativeTooltipSuppressionAuxiliaryControllerAwakePatch
{
    private static MethodBase TargetMethod() =>
        AccessTools.DeclaredMethod(typeof(AuxiliaryTooltipController), "Awake")
        ?? throw new MissingMethodException(typeof(AuxiliaryTooltipController).FullName, "Awake");

    [HarmonyPostfix]
    private static void Postfix() => NativeTooltipSuppression.NotifyControllerLifecycleChanged();
}

[HarmonyPatch(typeof(CardTooltipController), nameof(CardTooltipController.OnDestroy))]
internal static class NativeTooltipSuppressionCardControllerDestroyPatch
{
    [HarmonyPrefix]
    private static void Prefix() => NativeTooltipSuppression.NotifyControllerLifecycleChanged();
}

[HarmonyPatch(typeof(BaseTooltipController), nameof(BaseTooltipController.OnDestroy))]
internal static class NativeTooltipSuppressionAuxiliaryControllerDestroyPatch
{
    [HarmonyPrefix]
    private static void Prefix(BaseTooltipController __instance)
    {
        if (__instance is AuxiliaryTooltipController)
            NativeTooltipSuppression.NotifyControllerLifecycleChanged();
    }
}

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
