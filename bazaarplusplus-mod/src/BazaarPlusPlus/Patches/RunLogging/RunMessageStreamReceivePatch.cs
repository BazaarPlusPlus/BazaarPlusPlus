#nullable enable
#pragma warning disable CS0436
using System.Reflection;
using BazaarGameShared.Infra.Messages;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using HarmonyLib;

namespace BazaarPlusPlus.Patches.RunLogging;

/// <summary>Records each received message of the Run Message Stream (ADR-0028).</summary>
[HarmonyPatch]
internal static class RunMessageStreamReceivePatch
{
    private static MethodBase? TargetMethod() => NetMessageDispatchSeam.ResolveTarget();

    [HarmonyPrefix]
    private static void Prefix(INetMessage message, object[] __args)
    {
        try
        {
            if (message == null || NetMessageDispatchSeam.IsSpectatePlayback(__args))
                return;
            RunMessageStreamTap.OnReceived(message);
        }
        catch (Exception ex)
        {
            BppLog.ErrorEvent(
                new BppLogEvent(
                    BppLogFeatureScope.RunLogging,
                    "run_logging.message_stream.receive_failed",
                    storm: []
                ),
                ex
            );
        }
    }
}
