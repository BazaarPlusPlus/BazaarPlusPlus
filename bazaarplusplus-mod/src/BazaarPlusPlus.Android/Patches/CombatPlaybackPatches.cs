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
    private static void Prefix(ref CombatSimHandler? __instance, ref float __0)
    {
        // Native calls on Android pass null for this instance method, which only changes static
        // speed state. The interop wrapper rejects null before calling the original. Supply the
        // game's existing handler so the native method (including its speed event) still runs.
        __instance ??= AppState._combatSimHandler;
        var state = AndroidPatchHost.CombatStatus?.State;
        if (state?.ShouldOverrideCombatSpeed(__0) == true)
            __0 = state.CombatSpeedMultiplier;
    }
}
