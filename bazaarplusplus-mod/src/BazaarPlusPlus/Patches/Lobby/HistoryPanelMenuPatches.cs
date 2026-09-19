#nullable enable
using System.Reflection;
using HarmonyLib;
using TheBazaar.UI;
using TheBazaar.UI.Menu;

namespace BazaarPlusPlus.Patches.Lobby;

[HarmonyPatch(typeof(NavigationMenuController), "PopulateData")]
internal static class HistoryPanelMenuDataPatch
{
    [HarmonyPostfix]
    private static void Postfix(List<PoolableUIData> ____data) =>
        BppPatchHost.Features.HistoryMenu.Append(____data);
}

[HarmonyPatch]
internal static class HistoryPanelMenuPresentationPatch
{
    [HarmonyTargetMethods]
    private static IEnumerable<MethodBase> TargetMethods() =>
        new[] { "SetData", "OnEnable", "OnLocaleChanged" }.Select(name =>
            AccessTools.Method(typeof(SceneSelectOptionContainer), name)
        );

    [HarmonyPostfix]
    private static void Postfix(SceneSelectOptionContainer __instance) =>
        BppPatchHost.Features.HistoryMenu.Refresh(__instance);
}

[HarmonyPatch(typeof(SceneSelectOptionContainer), "SceneSelected")]
internal static class HistoryPanelMenuSelectedPatch
{
    [HarmonyPrefix]
    private static bool Prefix(SceneSelectOptionContainer __instance, bool selected) =>
        BppPatchHost.Features.HistoryMenu.Select(__instance, selected);
}
