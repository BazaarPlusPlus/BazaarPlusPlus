#nullable enable
using HarmonyLib;
using TheBazaar.Game.CardFrames;

namespace BazaarPlusPlus.Patches.Combat;

[HarmonyPatch(typeof(PriceTagContainer), "PlayPriceChangeVFX")]
internal static class CombatReplayPriceTagVfxPatch
{
    [HarmonyPrefix]
    private static bool Prefix()
    {
        return !CombatReplayPatchGuard.IsReplayStartOrPlaybackActive;
    }
}
