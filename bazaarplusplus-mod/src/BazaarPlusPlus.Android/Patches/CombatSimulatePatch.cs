using BazaarPlusPlus.Core.Events;
using BazaarPlusPlus.Core.GameState;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using HarmonyLib;
using Il2CppBazaarGameShared.Infra.Messages;
using Il2CppTheBazaar;

namespace BazaarPlusPlus.Android.Patches;

// A Harmony postfix on Simulate runs at its first async suspension, before combat has finished.
[HarmonyPatch(
    typeof(CombatSimHandler),
    nameof(CombatSimHandler.Simulate),
    typeof(NetMessageCombatSim),
    typeof(Il2CppSystem.Threading.CancellationTokenSource)
)]
internal static class CombatSimulatePatch
{
    [HarmonyPrefix]
    private static void Prefix(NetMessageCombatSim __0)
    {
        var hits = AndroidPatchHost.CountCombatSim();
        try
        {
            BppLog.InfoEvent(
                new BppLogEvent(BppLogFeatureScope.Android, "android.combat_sim.hit"),
                ("hits", hits),
                ("message", __0 != null)
            );
            AndroidPatchHost.Features?.CombatStatus?.State.SetCombatFrameTotal(
                __0?.Data?.Frames?.Count ?? 0
            );
            if (__0 != null)
                AndroidPatchHost.Composition?.EventBus.Publish(
                    new CombatSimObserved(
                        new CombatSimSnapshot(__0.MessageId, __0.Data?.Frames?.Count ?? 0)
                    )
                );
        }
        catch (Exception exception)
        {
            AndroidPatchHost.ReportProbeError("combat_sim", exception);
        }
    }
}
