#nullable enable
using BazaarPlusPlus.Game.CombatReplay;
using HarmonyLib;
using TheBazaar;

namespace BazaarPlusPlus.Patches.Combat;

[HarmonyPatch(typeof(ReplayState), nameof(ReplayState.Exit))]
internal static class CombatReplayStateExitPatch
{
    [HarmonyPrefix]
    private static bool Prefix()
    {
        return !CombatReplayRuntime.TryExitBootstrappedSavedReplayToMenu();
    }
}
