#nullable enable
using System.Threading.Tasks;
using HarmonyLib;
using TheBazaar;

namespace BazaarPlusPlus.Patches.Combat;

[HarmonyPatch(typeof(StartRunAppState), nameof(StartRunAppState.FinalizeRunInitialization))]
internal static class CombatReplayFinalizeRunInitializationPatch
{
    [HarmonyPrefix]
    private static bool Prefix(ref Task __result)
    {
        if (!CombatReplayPatchGuard.IsReplayStartOrPlaybackActive)
            return true;

        __result = Task.CompletedTask;
        return false;
    }
}
