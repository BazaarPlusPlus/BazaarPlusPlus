#pragma warning disable CS0436
#nullable enable
using HarmonyLib;
using TheBazaar.UI.EndOfRun;

namespace BazaarPlusPlus.Patches.EndOfRun;

[HarmonyPatch(typeof(EndOfRunScreenController), "OnContinueClick")]
internal static class EndOfRunScreenshotPatch
{
    [HarmonyPrefix]
    private static bool Prefix(EndOfRunScreenController __instance)
    {
        try
        {
            return !BppPatchHost.TryGetFeatures(out var features)
                || !features!.EndOfRunCaptureWorkflow.ShouldBlockContinue(__instance);
        }
        catch
        {
            return true;
        }
    }
}
