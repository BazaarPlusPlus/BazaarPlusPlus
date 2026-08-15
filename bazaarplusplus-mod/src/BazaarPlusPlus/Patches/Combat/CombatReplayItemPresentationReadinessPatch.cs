#nullable enable
#pragma warning disable CS0436

using BazaarGameClient.Domain.Models.Cards;
using BazaarPlusPlus.Game.CombatReplay.Bootstrap;
using HarmonyLib;
using TheBazaar;

namespace BazaarPlusPlus.Patches.Combat;

[HarmonyPatch]
internal static class CombatReplayItemPresentationReadinessPatch
{
    [HarmonyPatch(typeof(ItemController), nameof(ItemController.Setup), [typeof(Card)])]
    [HarmonyPostfix]
    private static void TrackItemSetup(ref Task __result) =>
        __result = ReplayItemPresentationReadiness.Track(__result);
}

[HarmonyPatch(typeof(ReplayState), "SpawnCombatCards", [])]
internal static class CombatReplayBoardSpawnReadinessPatch
{
    [HarmonyPostfix]
    private static void TrackBoardSpawn(ref Task __result) =>
        __result = ReplayItemPresentationReadiness.Track(__result);
}
