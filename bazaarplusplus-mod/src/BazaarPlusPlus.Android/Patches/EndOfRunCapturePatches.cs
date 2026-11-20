using HarmonyLib;
using Il2CppTheBazaar.UI.EndOfRun;

namespace BazaarPlusPlus.Android.Patches;

[HarmonyPatch(typeof(EndOfRunScreenController), nameof(EndOfRunScreenController.OnContinueClick))]
internal static class EndOfRunContinuePatch
{
    [HarmonyPrefix]
    private static bool Prefix(EndOfRunScreenController __instance)
    {
        try
        {
            return AndroidPatchHost.Features?.EndOfRunCapture is not { } capture
                || !capture.ShouldBlockContinue(__instance);
        }
        catch (Exception exception)
        {
            AndroidPatchHost.ReportProbeError("end_of_run_continue", exception);
            return true;
        }
    }
}

[HarmonyPatch(
    typeof(EndOfRunSummaryController),
    nameof(EndOfRunSummaryController.DisplayCardsAsync)
)]
internal static class EndOfRunRevealPatch
{
    // An async postfix runs at the first suspension. The card container is already visible at
    // entry.
    [HarmonyPrefix]
    private static void Prefix(EndOfRunSummaryController __instance)
    {
        try
        {
            AndroidPatchHost.Features?.EndOfRunCapture?.ObserveRevealStarted(__instance);
        }
        catch (Exception exception)
        {
            AndroidPatchHost.ReportProbeError("end_of_run_reveal", exception);
        }
    }
}
