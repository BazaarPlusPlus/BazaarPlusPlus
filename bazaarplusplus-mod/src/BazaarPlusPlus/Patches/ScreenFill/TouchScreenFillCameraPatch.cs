#nullable enable
using BazaarPlusPlus.Game.Touch;
using BazaarPlusPlus.GameInterop.Touch;
using HarmonyLib;
using Unity.Cinemachine;

namespace BazaarPlusPlus.Patches.ScreenFill;

/// <summary>
/// Cinemachine's brain writes its lens and position to the output camera in <c>LateUpdate</c>;
/// once it has, <see cref="TouchScreenFill.AfterCameraUpdate"/> widens the view for a screen
/// narrower than 16:10 (mobile platform only, ADR-0024). The argument-less <c>LateUpdate</c> is
/// hooked rather than <c>PushStateToUnityCamera</c>, as on the Android host, whose
/// <c>ref CameraState</c> argument crashed the IL2CPP trampoline. On iOS the postfix runs through
/// <c>Host/Ios/InterpreterMethodPatcher</c> like every other patch.
/// </summary>
[HarmonyPatch(typeof(CinemachineBrain), "LateUpdate")]
internal static class TouchScreenFillCameraPatch
{
    [HarmonyPrepare]
    private static bool Prepare() => TouchPlatform.IsMobile;

    [HarmonyPostfix]
    private static void Postfix(CinemachineBrain __instance) =>
        TouchScreenFill.AfterCameraUpdate(__instance.OutputCamera);
}
