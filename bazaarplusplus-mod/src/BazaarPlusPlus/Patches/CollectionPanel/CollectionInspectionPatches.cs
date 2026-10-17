#nullable enable
using System.Reflection;
using HarmonyLib;
using TheBazaar.Tooltips;
using TheBazaar.UI.Tooltips;

namespace BazaarPlusPlus.Patches.CollectionPanel;

[HarmonyPatch(typeof(CardTooltipController), nameof(CardTooltipController.Lock))]
internal static class CollectionInspectionLockPatch
{
    [HarmonyPostfix]
    private static void Postfix(CardTooltipController __instance) =>
        BppPatchHost.Features.CollectionInspection.OnLocked(__instance);
}

[HarmonyPatch]
internal static class CollectionInspectionPreviewPatch
{
    [HarmonyTargetMethods]
    private static IEnumerable<MethodBase> TargetMethods()
    {
        var method = AccessTools.Method(typeof(CardTooltipController), "ApplyPreviewTooltip");
        if (method != null)
            yield return method;
    }

    [HarmonyPostfix]
    private static void Postfix(CardTooltipController __instance, CardTooltipData tooltipData) =>
        BppPatchHost.Features.CollectionInspection.OnPreviewChanged(__instance, tooltipData);
}

[HarmonyPatch]
internal static class CollectionInspectionClosePatch
{
    [HarmonyTargetMethods]
    private static IEnumerable<MethodBase> TargetMethods() =>
        new[] { "Unlock", "OnDisable", "OnDestroy" }
            .Select(name => AccessTools.Method(typeof(CardTooltipController), name))
            .Where(method => method != null);

    // Restore canvas ownership before native destruction can destroy a borrowed hierarchy.
    [HarmonyPrefix]
    private static void Prefix(CardTooltipController __instance, MethodBase __originalMethod) =>
        BppPatchHost.Features.CollectionInspection.OnNativeClosed(
            __instance,
            unlock: __originalMethod.Name != "Unlock"
        );
}

[HarmonyPatch(typeof(CardTooltipController), nameof(CardTooltipController.StartTooltipFadeOut))]
internal static class CollectionInspectionFadeOutPatch
{
    [HarmonyPostfix]
    private static void Postfix(CardTooltipController __instance)
    {
        if (!__instance.IsLocked)
            BppPatchHost.Features.CollectionInspection.OnNativeClosed(__instance);
    }
}
