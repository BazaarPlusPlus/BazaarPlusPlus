using HarmonyLib;
using Il2CppNetworking;

namespace BazaarPlusPlus.Android.Patches;

[HarmonyPatch(typeof(ServersHealthService), nameof(ServersHealthService.IsUpdateRequired))]
internal static class UpdateRequiredPatch
{
    [HarmonyPostfix]
    private static void Postfix(bool __result)
    {
        if (__result && AndroidPatchHost.Mode == AndroidPatchMode.Enabled)
            AndroidPatchHost.Features?.UpdatePrompt?.GameRequiresUpdate();
    }
}
