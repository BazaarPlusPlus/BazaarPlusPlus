using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using HarmonyLib;
using Il2CppBazaarGameShared.Infra.Messages;
using Il2CppTheBazaar;

namespace BazaarPlusPlus.Android.Patches;

/// <summary>
/// Observes entry into combat simulation. <c>Simulate</c> is async and returns an
/// <c>Il2CppSystem.Threading.Tasks.Task</c>, so a postfix here would not mean the combat ended;
/// this hook only records that it started.
/// </summary>
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
        }
        catch (Exception exception)
        {
            AndroidPatchHost.ReportProbeError("combat_sim", exception);
        }
    }
}
