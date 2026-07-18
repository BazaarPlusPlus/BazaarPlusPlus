#nullable enable
#pragma warning disable CS0436
using System.Reflection;
using BazaarGameShared.Infra.Messages;
using BazaarPlusPlus.Core.Events;
using HarmonyLib;

namespace BazaarPlusPlus.Patches.RunLogging;

[HarmonyPatch]
internal static class RunInitializedPatch
{
    private static MethodBase? TargetMethod() => NetMessageDispatchSeam.ResolveTarget();

    [HarmonyPrefix]
    private static void Prefix(INetMessage message, object[] __args)
    {
        if (NetMessageDispatchSeam.IsSpectatePlayback(__args))
            return;

        if (message is not NetMessageRunInitialized runInitialized)
            return;

        BppPatchHost.Services.EventBus.Publish(
            new RunInitializedObserved { RunId = runInitialized.RunId }
        );
    }
}
