#nullable enable
using HarmonyLib;
using TheBazaar.UI.EndOfRun;

namespace BazaarPlusPlus.Patches.EndOfRun;

[HarmonyPatch(typeof(EndOfRunSummaryController), "DisplayCardsAsync")]
internal static class EndOfRunSummaryRevealPatch
{
    [HarmonyPrefix]
    private static void Prefix(EndOfRunSummaryController __instance)
    {
        // This prefix runs after carpet unlock and before card and skill display.
        // An async postfix runs at the first suspension, before display has completed.
        try
        {
            if (BppPatchHost.TryGetFeatures(out var features))
                features!.EndOfRunCaptureWorkflow.ObserveRevealStarted(__instance);
        }
        catch
        {
            // Patches run before/after mount during startup and teardown; always fail open.
        }
    }
}
