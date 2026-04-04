#pragma warning disable CS0436
using BazaarGameShared.Infra.Messages;
using BazaarPlusPlus.Core.Events;
using BazaarPlusPlus.Core.Runtime;
using HarmonyLib;
using TheBazaar;

namespace BazaarPlusPlus;

[HarmonyPatch(typeof(NetMessageProcessor), "ReceiveOrQueue")]
internal static class CombatReplayCapturePatch
{
    [HarmonyPostfix]
    private static void Postfix(INetMessage message)
    {
        if (message is not NetMessageGameSim && message is not NetMessageCombatSim)
            return;

        BppRuntimeHost.EventBus.Publish(new NetMessageObserved { Message = message });
    }
}
