#pragma warning disable CS0436
using BazaarGameShared.Infra.Messages;
using BazaarPlusPlus.Core.Events;
using BazaarPlusPlus.Patches;
using HarmonyLib;
using TheBazaar;

namespace BazaarPlusPlus;

[HarmonyPatch(typeof(NetMessageProcessor), "ReceiveOrQueue")]
internal static class RunInitializedPatch
{
    [HarmonyPrefix]
    private static void Prefix(INetMessage message)
    {
        if (message is not NetMessageRunInitialized runInitialized)
            return;

        BppPatchHost.Services.EventBus.Publish(
            new RunInitializedObserved { RunId = runInitialized.RunId }
        );
    }
}
