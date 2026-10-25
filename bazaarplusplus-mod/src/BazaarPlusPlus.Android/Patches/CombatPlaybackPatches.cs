using HarmonyLib;
using Il2CppTheBazaar;

namespace BazaarPlusPlus.Android.Patches;

[HarmonyPatch(typeof(FinalBlowSlowDownController), nameof(FinalBlowSlowDownController.Process))]
internal static class CombatPlaybackFramePatch
{
    [HarmonyPostfix]
    private static void Postfix() => AndroidPatchHost.CombatStatus?.State.AdvanceCombatFrame();
}

[HarmonyPatch(typeof(CombatSimHandler), nameof(CombatSimHandler.SetSpeed))]
internal static class CombatPlaybackSpeedPatch
{
    [HarmonyPrefix]
    private static void Prefix(ref float __0)
    {
        var state = AndroidPatchHost.CombatStatus?.State;
        if (state?.ShouldOverrideCombatSpeed(__0) == true)
            __0 = state.CombatSpeedMultiplier;
    }
}
