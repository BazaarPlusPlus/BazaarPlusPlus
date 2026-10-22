using BazaarPlusPlus.Android.GameInterop;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using HarmonyLib;
using Il2CppTheBazaar.Tooltips;
using Il2CppTheBazaar.UI.Tooltips;

namespace BazaarPlusPlus.Android.Patches;

/// <summary>
/// The tooltip entry a tap opens on touch screens, and where the upgrade preview takes hold: the
/// card's controller enters the game's upgrade preview before its tooltip renders.
/// <c>CardController</c> lives in the global namespace of the game, so its proxy is
/// <c>Il2Cpp.CardController</c>.
/// </summary>
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
        if (AndroidPatchHost.Mode != AndroidPatchMode.Enabled)
            return;
        try
        {
            AndroidUpgradePreview.OnShowTooltips(__instance);
        }
        catch (Exception exception)
        {
            AndroidPatchHost.ReportProbeError("upgrade_preview", exception);
        }
    }
}

/// <summary>The card tooltip closed: the held upgrade preview ends with it.</summary>
[HarmonyPatch(
    typeof(TooltipParentComponent),
    nameof(TooltipParentComponent.HideCardTooltipController)
)]
internal static class CardTooltipHidePatch
{
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

/// <summary>
/// A card tooltip's contents render: the enchant preview section is built for this card and
/// handed to its passive block (<see cref="EnchantPreviewPassivePatch"/>), then dropped.
/// </summary>
[HarmonyPatch(typeof(CardTooltipTypeHandler), nameof(CardTooltipTypeHandler.RenderCardContents))]
internal static class EnchantPreviewRenderPatch
{
    [HarmonyPrefix]
    private static void Prefix(CardTooltipData tooltipData)
    {
        AndroidPatchHost.CountTooltipRender();
        if (AndroidPatchHost.Mode == AndroidPatchMode.Enabled)
            AndroidEnchantPreview.Prepare(tooltipData);
    }

    [HarmonyFinalizer]
    private static void Finalizer() => AndroidEnchantPreview.Clear();
}

/// <summary>Appends the pending enchant preview section below the native passive text.</summary>
[HarmonyPatch(typeof(EffectBlockRenderer), nameof(EffectBlockRenderer.RenderPassive))]
internal static class EnchantPreviewPassivePatch
{
    [HarmonyPostfix]
    private static void Postfix(EffectBlockRenderer __instance, string text)
    {
        var section = AndroidEnchantPreview.TakePending();
        if (section == null)
            return;
        try
        {
            __instance._passiveParentVisibility.SetVisible(true);
            __instance._dividerParent?.SetActive(true);
            __instance._passiveText.SetText(
                string.IsNullOrEmpty(text) ? section : $"{text}\n{section}"
            );
        }
        catch (Exception exception)
        {
            AndroidPatchHost.ReportProbeError("enchant_preview", exception);
        }
    }
}
