#nullable enable
#pragma warning disable CS0436
using System.Reflection;
using BazaarGameShared.Infra.Messages;
using BazaarPlusPlus.GameInterop.Events;
using HarmonyLib;

namespace BazaarPlusPlus.Patches.Combat;

[HarmonyPatch]
internal static class CombatReplayCapturePatch
{
    private static MethodBase? TargetMethod() => NetMessageDispatchSeam.ResolveTarget();

    [HarmonyPostfix]
    private static void Postfix(INetMessage message, object[] __args)
    {
        if (NetMessageDispatchSeam.IsSpectatePlayback(__args))
            return;

        if (message is not NetMessageGameSim && message is not NetMessageCombatSim)
            return;

        BppPatchHost.Services.EventBus.Publish(new NetMessageObserved { Message = message });
    }
}
