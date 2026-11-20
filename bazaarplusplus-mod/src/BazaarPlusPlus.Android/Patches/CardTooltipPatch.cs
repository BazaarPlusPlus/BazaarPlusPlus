using BazaarPlusPlus.Android.GameInterop;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using HarmonyLib;
using Il2CppBazaarGameClient.Domain.Tooltips;
using Il2CppTheBazaar.Tooltips;
using Il2CppTheBazaar.UI.Tooltips;

namespace BazaarPlusPlus.Android.Patches;

[HarmonyPatch(typeof(Il2Cpp.CardController), nameof(Il2Cpp.CardController.ShowTooltips))]
internal static class CardTooltipPatch
{
    private const long LoggedHits = 5;

    [HarmonyPrefix]
    private static void Prefix(Il2Cpp.CardController __instance)
    {
        var hits = AndroidPatchHost.CountTooltip();
        if (hits <= LoggedHits)
            BppLog.InfoEvent(
                new BppLogEvent(BppLogFeatureScope.Android, "android.tooltip.hit"),
                ("hits", hits)
            );
    }
}

[HarmonyPatch(
    typeof(TooltipParentComponent),
    nameof(TooltipParentComponent.HideCardTooltipController)
)]
internal static class CardTooltipHidePatch
{
    // Mobile input dismisses the native tooltip after the BPP button's onClick, even for touches
    // inside its own drawer.
    [HarmonyPrefix]
    private static bool Prefix() =>
        Il2CppTheBazaar.Data.IsInCombat
        || AndroidPatchHost.Features?.PreviewSwitches?.ContainsPointer() != true;

    [HarmonyPostfix]
    private static void Postfix(TooltipParentComponent __instance)
    {
        AndroidPatchHost.CountTooltipHidden();
        try
        {
            AndroidUpgradePreview.OnTooltipHidden(__instance);
        }
        catch (Exception exception)
        {
            AndroidPatchHost.ReportProbeError("upgrade_preview", exception);
        }
    }
}

[HarmonyPatch(typeof(CardTooltipTypeHandler), nameof(CardTooltipTypeHandler.RenderCardContents))]
internal static class CardTooltipRenderPatch
{
    [HarmonyPrefix]
    private static void Prefix(CardTooltipController controller, CardTooltipData tooltipData)
    {
        AndroidPatchHost.CountTooltipRender();
        if (AndroidPatchHost.Mode != AndroidPatchMode.Enabled)
            return;
        try
        {
            if (!controller.IsPositioningSecondaryTooltip)
            {
                var cardController = Il2CppTheBazaar.Data.CardAndSkillLookup?.GetCardController(
                    tooltipData.CardInstance
                );
                AndroidUpgradePreview.OnShowTooltips(cardController);
                AndroidPatchHost.Features?.TooltipPreviewHost?.Observe(controller, tooltipData);
            }
        }
        catch (Exception exception)
        {
            AndroidPatchHost.ReportProbeError("preview_switches", exception);
        }
        AndroidPatchHost.Features?.EnchantPreview?.Prepare(tooltipData);
        AndroidUpgradePreviewValues.Prepare(tooltipData);
    }

    [HarmonyFinalizer]
    private static void Finalizer()
    {
        AndroidPatchHost.Features?.EnchantPreview?.Clear();
        AndroidUpgradePreviewValues.Return();
    }
}

[HarmonyPatch(typeof(EffectBlockRenderer), nameof(EffectBlockRenderer.RenderPassive))]
internal static class EnchantPreviewPassivePatch
{
    [HarmonyPostfix]
    private static void Postfix(EffectBlockRenderer __instance, string text)
    {
        try
        {
            var section = AndroidPatchHost.Features?.EnchantPreview?.TakePending(
                __instance._passiveText.textObject
            );
            if (section == null)
                return;
            __instance._passiveParentVisibility.SetVisible(true);
            __instance._dividerParent?.SetActive(true);
            __instance._passiveText.SetText(
                string.IsNullOrEmpty(text) ? section : $"{text}\n{section}"
            );
            AndroidPatchHost.Features?.EnchantPreview?.Fit(__instance._passiveText.textObject);
        }
        catch (Exception exception)
        {
            AndroidPatchHost.ReportProbeError("enchant_preview", exception);
        }
    }
}

[HarmonyPatch(typeof(CardTooltipData), nameof(CardTooltipData.RenderTooltip))]
internal static class UpgradeValueTextPatch
{
    [HarmonyPrefix]
    private static bool Prefix(
        CardTooltipData __instance,
        TooltipBuilder builder,
        ref string __result
    )
    {
        try
        {
            if (!AndroidUpgradePreviewValues.TryRender(__instance, builder, out var rendered))
                return true;
            __result = rendered;
            return false;
        }
        catch (Exception exception)
        {
            AndroidPatchHost.ReportProbeError("upgrade_values", exception);
            return true;
        }
    }
}

[HarmonyPatch(typeof(CooldownRenderer), nameof(CooldownRenderer.RenderFromTooltip))]
internal static class UpgradeValueCooldownPatch
{
    [HarmonyPostfix]
    private static void Postfix(CooldownRenderer __instance, CardTooltipData tooltipData)
    {
        try
        {
            AndroidUpgradePreviewValues.OnCooldownRendered(__instance, tooltipData);
        }
        catch (Exception exception)
        {
            AndroidPatchHost.ReportProbeError("upgrade_values", exception);
        }
    }
}
