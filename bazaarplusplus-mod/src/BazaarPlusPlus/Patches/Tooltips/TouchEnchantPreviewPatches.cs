#nullable enable
using BazaarPlusPlus.Game.ItemEnchantPreview;
using BazaarPlusPlus.GameInterop.Touch;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using HarmonyLib;
using TheBazaar.Tooltips;
using TheBazaar.UI.Tooltips;

namespace BazaarPlusPlus.Patches.Tooltips;

/// <summary>
/// The Mobile UI's enchant preview in the native tooltip's main text on a mobile platform
/// (ADR-0024, ADR-0025), as the Android host's <c>CardTooltipRenderPatch</c> and
/// <c>EnchantPreviewPassivePatch</c>: a card tooltip's render builds the section
/// (<c>TouchEnchantPreview.Prepare</c>, through <see cref="TouchEnchantPreviewHost.Preview"/>), its
/// passive block appends it below the passive text, and the section ends with the render. The desktop's
/// own section (<c>BppTooltipSectionRenderPatch</c>) stays off on a mobile platform
/// (<c>TooltipPreviewModePolicy</c>).
/// </summary>
[HarmonyPatch]
internal static class TouchEnchantPreviewPatches
{
    private static int _failures;

    [HarmonyPrepare]
    private static bool Prepare() => TouchPlatform.IsMobile;

    [HarmonyPrefix]
    [HarmonyPatch(
        typeof(CardTooltipTypeHandler),
        nameof(CardTooltipTypeHandler.RenderCardContents)
    )]
    private static void Rendering(CardTooltipData tooltipData) =>
        TouchEnchantPreviewHost.Preview?.Prepare(tooltipData);

    [HarmonyFinalizer]
    [HarmonyPatch(
        typeof(CardTooltipTypeHandler),
        nameof(CardTooltipTypeHandler.RenderCardContents)
    )]
    private static void Rendered() => TouchEnchantPreviewHost.Preview?.Clear();

    [HarmonyPostfix]
    [HarmonyPatch(typeof(EffectBlockRenderer), nameof(EffectBlockRenderer.RenderPassive))]
    private static void Passive(EffectBlockRenderer __instance, string text)
    {
        try
        {
            var section = TouchEnchantPreviewHost.Preview?.TakePending(
                __instance._passiveText?.textObject
            );
            if (section == null)
                return;
            __instance._passiveParentVisibility.SetVisible(true);
            if (__instance._dividerParent != null)
                __instance._dividerParent.SetActive(true);
            __instance._passiveText!.SetText(
                string.IsNullOrEmpty(text) ? section : $"{text}\n{section}"
            );
        }
        catch (Exception exception)
        {
            if (++_failures <= 5)
                BppLog.WarnEvent(
                    new BppLogEvent(
                        BppLogFeatureScope.Touch,
                        "touch.enchant_preview.append_failed"
                    ),
                    exception
                );
        }
    }
}
