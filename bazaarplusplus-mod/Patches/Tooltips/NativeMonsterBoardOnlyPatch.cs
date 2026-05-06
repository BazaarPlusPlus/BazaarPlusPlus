#pragma warning disable CS0436
#nullable enable
using BazaarPlusPlus.Game.Tooltips;
using HarmonyLib;
using TheBazaar.Tooltips;
using TheBazaar.UI.Tooltips;

namespace BazaarPlusPlus;

[HarmonyPatch(typeof(CardTooltipTypeHandler), nameof(CardTooltipTypeHandler.HandleTooltip))]
internal static class NativeMonsterBoardOnlyHandleTooltipPatch
{
    [HarmonyPostfix]
    private static void Postfix(CardTooltipController controller, ITooltipData tooltipData)
    {
        NativeMonsterBoardOnlyFormatter.Apply(controller, "handle_tooltip");
    }
}

[HarmonyPatch(typeof(DesktopLockModeController), nameof(DesktopLockModeController.Lock))]
internal static class NativeMonsterBoardOnlyDesktopLockPatch
{
    [HarmonyPostfix]
    private static void Postfix(CardTooltipController controller)
    {
        NativeMonsterBoardOnlyFormatter.Apply(controller, "desktop_lock");
    }
}

[HarmonyPatch(typeof(MobileLockModeController), nameof(MobileLockModeController.Lock))]
internal static class NativeMonsterBoardOnlyMobileLockPatch
{
    [HarmonyPostfix]
    private static void Postfix(CardTooltipController controller)
    {
        NativeMonsterBoardOnlyFormatter.Apply(controller, "mobile_lock");
    }
}

[HarmonyPatch(typeof(DesktopLockModeController), "RefreshLockedLegendVisibility")]
internal static class NativeMonsterBoardOnlyDesktopLegendRefreshPatch
{
    [HarmonyPostfix]
    private static void Postfix(CardTooltipController controller)
    {
        NativeMonsterBoardOnlyFormatter.Apply(controller, "desktop_refresh_locked_legend");
    }
}

[HarmonyPatch(typeof(MobileLockModeController), "RefreshLockedLegendVisibility")]
internal static class NativeMonsterBoardOnlyMobileLegendRefreshPatch
{
    [HarmonyPostfix]
    private static void Postfix(CardTooltipController controller)
    {
        NativeMonsterBoardOnlyFormatter.Apply(controller, "mobile_refresh_locked_legend");
    }
}

[HarmonyPatch(typeof(CardTooltipController), "SetupLockVariantPanel")]
internal static class NativeMonsterBoardOnlySetupLockVariantPanelPatch
{
    [HarmonyPrefix]
    private static bool Prefix(CardTooltipController __instance)
    {
        if (!NativeMonsterBoardOnlyFormatter.ShouldUseBoardOnly(__instance))
            return true;

        NativeMonsterBoardOnlyFormatter.Apply(__instance, "skip_lock_variant_panel");
        return false;
    }
}
