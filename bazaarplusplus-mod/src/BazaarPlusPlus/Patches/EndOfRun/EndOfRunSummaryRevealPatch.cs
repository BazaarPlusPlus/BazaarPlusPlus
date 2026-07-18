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
        // This method is invoked only after the native carpet unlock has completed and the card
        // container is visible; skill display follows in the same OnLoadShowCards call. A prefix
        // is intentional: an async postfix would run at the first suspension, not completion.
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
