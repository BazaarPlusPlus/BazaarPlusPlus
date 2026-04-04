#pragma warning disable CS0436
using System.Threading;
using BazaarGameShared.Infra.Messages;
using BazaarPlusPlus.Core.Events;
using BazaarPlusPlus.Core.Runtime;
using HarmonyLib;
using TheBazaar;

namespace BazaarPlusPlus;

// Combat sim: capture win/loss result
[HarmonyPatch(typeof(CombatSimHandler), "Simulate")]
class CombatSimPatch
{
    [HarmonyPrefix]
    static void Prefix(NetMessageCombatSim message, CancellationTokenSource cancellationToken)
    {
        BppRuntimeHost.EventBus.Publish(new CombatSimObserved { Message = message });
    }
}

[HarmonyPatch(typeof(FinalBlowSlowDownController), nameof(FinalBlowSlowDownController.Process))]
class CombatFrameAdvancePatch
{
    [HarmonyPostfix]
    static void Postfix()
    {
        BppRuntimeHost.EventBus.Publish(new CombatFrameAdvanced());
    }
}
