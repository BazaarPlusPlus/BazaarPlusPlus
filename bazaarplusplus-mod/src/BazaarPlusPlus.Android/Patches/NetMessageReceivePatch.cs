using BazaarPlusPlus.Android.CombatReplay;
using BazaarPlusPlus.Core.Events;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using HarmonyLib;
using Il2CppBazaarGameShared.Infra.Messages;
using Il2CppInterop.Runtime;
using Il2CppTheBazaar;

namespace BazaarPlusPlus.Android.Patches;

// ReceiveOrQueue recurses into aggregate children, so this hook sees them too.
// IL2CPP interface proxies require TryCast because managed is tests miss them.
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
            var nativeClass = IL2CPP.il2cpp_object_get_class(__0.Pointer);
            var nativeType = AndroidPatchHost.NameMessageType(nativeClass, __0);
            var hits = AndroidPatchHost.CountMessage(nativeClass);
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
            var replayActive = AndroidReplayGuard.IsActive;
            var decision = AndroidReplayGuard.Decide(replayActive);
            if (replayActive)
            {
                var seen = AndroidReplayGuard.CountIntakeMessage();
                if (seen <= LoggedHitsPerType)
                    BppLog.InfoEvent(
                        new BppLogEvent(
                            BppLogFeatureScope.Android,
                            "android.replay_guard.message_seen"
                        ),
                        ("native_type", __0.Cast<Il2CppSystem.Object>().GetIl2CppType().FullName),
                        ("seen", seen)
                    );
            }
            var runInitialized = decision.PublishRunInitialized
                ? __0.TryCast<NetMessageRunInitialized>()
                : null;
            if (runInitialized != null)
            {
                var hits = AndroidPatchHost.CountRunInitialized();
                BppLog.InfoEvent(
                    new BppLogEvent(BppLogFeatureScope.Android, "android.run_initialized.hit"),
                    ("hits", hits),
                    ("run_id", runInitialized.RunId, BppLogCorrelationPolicy.Short)
                );
                composition.EventBus.Publish(
                    new RunInitializedObserved
                    {
                        RunId = runInitialized.RunId,
                        BuildId = runInitialized.BuildId,
                    }
                );
            }

            if (decision.CaptureBattle)
                composition.BattleCapture.Accept(__0);
            else
                AndroidReplayGuard.CountCaptureSkip();
        }
        catch (Exception exception)
        {
            AndroidPatchHost.ReportProbeError("record_chain", exception);
        }
    }
}
