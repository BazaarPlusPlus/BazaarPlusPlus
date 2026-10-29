using BazaarPlusPlus.Android.GameInterop;
using HarmonyLib;
using Unity.Cinemachine;

namespace BazaarPlusPlus.Android.Patches;

/// <summary>
/// Cinemachine's brain writes its lens to the output camera in <c>LateUpdate</c>; once it has,
/// <see cref="AndroidScreenFill.WidenFieldOfView"/> widens it for a screen narrower than 16:10.
/// <c>PushStateToUnityCamera</c> itself is not hooked: its <c>ref CameraState</c> argument crashes
/// the native trampoline as the Gameplay scene loads.
/// </summary>
[HarmonyPatch(typeof(CinemachineBrain), nameof(CinemachineBrain.LateUpdate))]
internal static class ScreenFillFieldOfViewPatch
{
    [HarmonyPostfix]
    private static void Postfix(CinemachineBrain __instance)
    {
        try
        {
            AndroidScreenFill.WidenFieldOfView(__instance.OutputCamera);
        }
        catch (Exception exception)
        {
            AndroidPatchHost.ReportProbeError("screen_fill_fov", exception);
        }
    }
}
