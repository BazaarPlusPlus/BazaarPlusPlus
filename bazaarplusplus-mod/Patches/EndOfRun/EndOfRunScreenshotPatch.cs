#pragma warning disable CS0436
#nullable enable
using BazaarPlusPlus.Game.Screenshots;
using HarmonyLib;
using TheBazaar.UI.EndOfRun;

namespace BazaarPlusPlus;

[HarmonyPatch(typeof(EndOfRunScreenController), "OnContinueClick")]
internal static class EndOfRunScreenshotPatch
{
    [HarmonyPrefix]
    private static bool Prefix(EndOfRunScreenController __instance)
    {
        if (EndOfRunScreenshotController.TryConsumeContinuePassthrough())
            return true;

        if (EndOfRunScreenshotController.ShouldSuppressContinueWhileCaptureInFlight())
            return false;

        if (EndOfRunScreenshotController.ShouldBlockContinueUntilFirstCapture(__instance))
            return false;

        var captureIntercepted = EndOfRunScreenshotController.TryCaptureFirstContinue(
            __instance,
            isInteractionBlocked: false
        );
        return !captureIntercepted;
    }
}
