using HarmonyLib;

namespace BazaarPlusPlus.Patches.Input;

[HarmonyPatch(typeof(MobileSkillOverlayListBuilder), nameof(MobileSkillOverlayListBuilder.Build))]
internal static class DesktopSkillOverlayListLayoutPatch
{
    [HarmonyPrefix]
    private static void Prefix(MobileSkillOverlayListBuilder __instance) =>
        BppPatchHost.Features.SkillOverlayLayout.PrepareList(__instance);
}

[HarmonyPatch(typeof(MobileSkillOverlayListBuilder), "DrawLegend")]
internal static class DesktopSkillOverlayLegendLayoutPatch
{
    [HarmonyPostfix]
    private static void Postfix(MobileSkillOverlayListBuilder __instance) =>
        BppPatchHost.Features.SkillOverlayLayout.PrepareLegend(__instance);
}

[HarmonyPatch(typeof(MobileSkillOverlayRowView), nameof(MobileSkillOverlayRowView.Bind))]
internal static class DesktopSkillOverlayRowLayoutPatch
{
    [HarmonyPrefix]
    private static void Prefix(MobileSkillOverlayRowView __instance) =>
        BppPatchHost.Features.SkillOverlayLayout.PrepareRow(__instance);
}

[HarmonyPatch(
    typeof(MobileSkillOverlayRowView),
    nameof(MobileSkillOverlayRowView.SetSeparatorActive)
)]
internal static class DesktopSkillOverlaySeparatorPatch
{
    [HarmonyPrefix]
    private static void Prefix(ref bool isActive) => isActive = false;
}
