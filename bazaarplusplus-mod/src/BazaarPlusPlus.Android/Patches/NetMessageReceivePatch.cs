using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using HarmonyLib;
using Il2CppBazaarGameShared.Infra.Messages;
using Il2CppTheBazaar;

namespace BazaarPlusPlus.Android.Patches;

/// <summary>
/// Counts every server message by its native type at the client's single intake. An interface
/// proxy never satisfies <c>is</c>; <c>TryCast</c> is the IL2CPP form of the desktop's type test.
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
    }
}
