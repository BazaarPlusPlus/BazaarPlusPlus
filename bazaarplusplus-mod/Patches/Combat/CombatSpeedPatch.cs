#pragma warning disable CS0436
using BazaarPlusPlus.Game.CombatStatusBar;
using HarmonyLib;
using TheBazaar;

namespace BazaarPlusPlus;

[HarmonyPatch(typeof(CombatSimHandler), "SetSpeed")]
internal static class CombatSpeedPatch
{
    [HarmonyPrefix]
    private static void Prefix(ref float speed)
    {
        if (!CombatStatusBar.ShouldOverrideCombatSpeed(speed))
            return;

        speed = CombatStatusBar.CombatSpeedMultiplier;
    }
}
