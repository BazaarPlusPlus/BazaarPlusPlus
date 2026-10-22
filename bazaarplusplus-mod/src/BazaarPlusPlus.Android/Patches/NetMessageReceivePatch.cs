using BazaarPlusPlus.Core.Events;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using HarmonyLib;
using Il2CppBazaarGameShared.Infra.Messages;
using Il2CppTheBazaar;

namespace BazaarPlusPlus.Android.Patches;

/// <summary>
/// The client's single message intake, the Android form of the desktop's
/// <c>NetMessageDispatchSeam</c>: online <c>ReceiveOrQueue</c> recurses into aggregate children, so
/// every message passes here. It counts each by native type, publishes the run initialization the
/// desktop's <c>RunInitializedPatch</c> does, and feeds the PvP battle capture. An interface proxy
/// never satisfies <c>is</c>; <c>TryCast</c> is the IL2CPP form of the desktop's type test.
/// </summary>
[HarmonyPatch(typeof(NetMessageProcessor), nameof(NetMessageProcessor.ReceiveOrQueue))]
internal static class NetMessageReceivePatch
{
    private const long LoggedHitsPerType = 3;

    [HarmonyPrefix]
    private static void Prefix(INetMessage __0)
    {
        try
        {
            if (__0 == null)
                return;
            var nativeType = __0.Cast<Il2CppSystem.Object>().GetIl2CppType().FullName;
            var hits = AndroidPatchHost.CountMessage(nativeType);
            if (hits <= LoggedHitsPerType)
                BppLog.InfoEvent(
                    new BppLogEvent(BppLogFeatureScope.Android, "android.net_message.hit"),
                    ("native_type", nativeType),
                    ("hits", hits),
                    ("combat_sim", __0.TryCast<NetMessageCombatSim>() != null)
                );
        }
        catch (Exception exception)
        {
            AndroidPatchHost.ReportProbeError("net_message", exception);
        }

        var composition = AndroidPatchHost.Composition;
        if (__0 == null || composition == null)
            return;
        try
        {
            var runInitialized = __0.TryCast<NetMessageRunInitialized>();
            if (runInitialized != null)
            {
                var hits = AndroidPatchHost.CountRunInitialized();
                BppLog.InfoEvent(
                    new BppLogEvent(BppLogFeatureScope.Android, "android.run_initialized.hit"),
                    ("hits", hits),
                    ("run_id", runInitialized.RunId, BppLogCorrelationPolicy.Short)
                );
                composition.EventBus.Publish(
                    new RunInitializedObserved { RunId = runInitialized.RunId }
                );
            }

            composition.BattleCapture.Accept(__0);
        }
        catch (Exception exception)
        {
            AndroidPatchHost.ReportProbeError("record_chain", exception);
        }
    }
}
