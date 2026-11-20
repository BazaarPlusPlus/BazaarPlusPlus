using HarmonyLib;
using Unity.Cinemachine;

namespace BazaarPlusPlus.Android.Patches;

// Detouring PushStateToUnityCamera with its ref CameraState crashes during Gameplay scene load.
[HarmonyPatch(typeof(CinemachineBrain), nameof(CinemachineBrain.LateUpdate))]
internal static class ScreenFillFieldOfViewPatch
{
    [HarmonyPostfix]
    private static void Postfix(CinemachineBrain __instance)
    {
        try
        {
            AndroidPatchHost.Features?.ScreenFill?.AfterCameraUpdate(__instance.OutputCamera);
        }
        catch (Exception exception)
        {
            AndroidPatchHost.ReportProbeError("screen_fill_fov", exception);
        }
    }
}
