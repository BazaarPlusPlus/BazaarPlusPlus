#nullable enable
#pragma warning disable CS0436
using BazaarGameShared.Infra.Messages;
using BazaarPlusPlus.Core.Events;
using BazaarPlusPlus.Core.GameState;
using BazaarPlusPlus.Game.CombatReplay;
using HarmonyLib;
using TheBazaar;

namespace BazaarPlusPlus.Patches.Combat;

// Combat sim: capture win/loss result
[HarmonyPatch(typeof(CombatSimHandler), "Simulate")]
internal class CombatSimPatch
{
    [HarmonyPrefix]
    private static bool Prefix(
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

        // Copy what Core consumers read here, on the game thread, before the simulation runs.
        BppPatchHost.Services.EventBus.Publish(
            new CombatSimObserved(
                new CombatSimSnapshot(message.MessageId, message.Data?.Frames?.Count ?? 0)
            )
        );
        BppPatchHost.Features.PostCombatImpact.ObserveCombatSimulation(message.Data);
        return true;
    }
}

[HarmonyPatch(typeof(FinalBlowSlowDownController), nameof(FinalBlowSlowDownController.Process))]
internal class CombatFrameAdvancePatch
{
    [HarmonyPostfix]
    private static void Postfix()
    {
        BppPatchHost.Services.EventBus.Publish(CombatFrameAdvanced.Instance);
    }
}
