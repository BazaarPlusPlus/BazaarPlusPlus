#nullable enable
#pragma warning disable CS0436
using BazaarGameShared.Infra.Messages;
using BazaarPlusPlus.GameInterop.Events;
using BazaarPlusPlus.Patches;
using HarmonyLib;
using TheBazaar;

namespace BazaarPlusPlus.Patches.Combat;

[HarmonyPatch(typeof(NetMessageProcessor), "ReceiveOrQueue")]
internal static class CombatReplayCapturePatch
{
    [HarmonyPostfix]
    private static void Postfix(INetMessage message)
    {
        if (message is not NetMessageGameSim && message is not NetMessageCombatSim)
            return;

        BppPatchHost.Services.EventBus.Publish(new NetMessageObserved { Message = message });
    }
}
