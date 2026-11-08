using HarmonyLib;
using Il2CppTheBazaar.UI.EndOfRun;

namespace BazaarPlusPlus.Android.Patches;

/// <summary>
/// The end-of-run screenshot's two intents, as the desktop's patches of the same targets: the
/// Continue click waits while the capture is pending, and the summary's card display marks the
/// reveal the capture waits on. Both fail open, and do nothing without the interaction features.
/// </summary>
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
    // A prefix: the native card container is visible by now, and an async method's postfix
    // would run at its first suspension, not its completion.
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
