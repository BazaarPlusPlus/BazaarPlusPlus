#nullable enable
#pragma warning disable CS0436
using BazaarGameShared.Infra.Messages;
using BazaarPlusPlus.Core.Events;
using BazaarPlusPlus.Game.CombatReplay;
using BazaarPlusPlus.GameInterop.Events;
using HarmonyLib;
using TheBazaar;

namespace BazaarPlusPlus.Patches.Combat;

// Combat sim: capture win/loss result
[HarmonyPatch(typeof(CombatSimHandler), "Simulate")]
class CombatSimPatch
{
    [HarmonyPrefix]
    static bool Prefix(
        CombatSimHandler __instance,
        NetMessageCombatSim message,
        CancellationTokenSource cancellationToken,
        ref Task __result
    )
    {
        if (
            CombatReplayRuntime.Instance?.TryDeferCurrentReplaySimulation(
                __instance,
                message,
                cancellationToken,
                out var deferredSimulation
            ) == true
        )
        {
            __result = deferredSimulation;
            return false;
        }

        BppPatchHost.Services.EventBus.Publish(new CombatSimObserved { Message = message });
        return true;
    }
}

[HarmonyPatch(typeof(FinalBlowSlowDownController), nameof(FinalBlowSlowDownController.Process))]
class CombatFrameAdvancePatch
{
    [HarmonyPostfix]
    static void Postfix()
    {
        BppPatchHost.Services.EventBus.Publish(CombatFrameAdvanced.Instance);
    }
}
