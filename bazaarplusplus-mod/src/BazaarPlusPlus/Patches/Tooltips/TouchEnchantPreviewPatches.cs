#nullable enable
using BazaarPlusPlus.Game.Touch;
using BazaarPlusPlus.GameInterop.Touch;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using HarmonyLib;
using TheBazaar.Tooltips;
using TheBazaar.UI.Tooltips;

namespace BazaarPlusPlus.Patches.Tooltips;

/// <summary>
/// The enchant preview in the native tooltip's main text on a mobile platform (ADR-0024), as the
/// Android host's <c>CardTooltipRenderPatch</c> and <c>EnchantPreviewPassivePatch</c>: a card
/// tooltip's render builds the section (<see cref="TouchEnchantPreview.Prepare"/>), its passive
/// block appends it below the passive text, and the section ends with the render. The desktop's
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
        TouchEnchantPreview.Prepare(tooltipData);

    [HarmonyFinalizer]
    [HarmonyPatch(
        typeof(CardTooltipTypeHandler),
        nameof(CardTooltipTypeHandler.RenderCardContents)
    )]
    private static void Rendered() => TouchEnchantPreview.Clear();

    [HarmonyPostfix]
    [HarmonyPatch(typeof(EffectBlockRenderer), nameof(EffectBlockRenderer.RenderPassive))]
    private static void Passive(EffectBlockRenderer __instance, string text)
    {
        try
        {
            var section = TouchEnchantPreview.TakePending(
                RenderedFontPixels(__instance._passiveText?.textObject)
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

    // The label's font size in screen pixels: its canvas units, scaled by its transform within
    // the canvas and by the canvas's own scale factor. Zero when it cannot be read.
    private static float RenderedFontPixels(TMP_Text? label)
    {
        if (label == null)
            return 0f;
        var canvas = label.canvas != null ? label.canvas.rootCanvas : null;
        if (canvas == null)
            return 0f;
        var canvasScale = canvas.transform.lossyScale.y;
        var relative = canvasScale > 0f ? label.transform.lossyScale.y / canvasScale : 1f;
        return label.fontSize * relative * canvas.scaleFactor;
    }
}
