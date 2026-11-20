using System.Runtime.InteropServices;
using BazaarPlusPlus.Infrastructure.Logging;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.Injection;
using Il2CppInterop.Runtime.Runtime;
using Il2CppInterop.Runtime.Startup;
using Il2CppTheBazaar;

namespace BazaarPlusPlus.Android.Patches;

[HarmonyPatch(typeof(FinalBlowSlowDownController), nameof(FinalBlowSlowDownController.Process))]
internal static class CombatPlaybackFramePatch
{
    [HarmonyPostfix]
    private static void Postfix() =>
        AndroidPatchHost.Features?.CombatStatus?.State.AdvanceCombatFrame();
}

// Native callers can leave a non-object value in SetSpeed's unused receiver register.
// Harmony would wrap it before the prefix runs, so keep it opaque in a native detour.
internal static unsafe class CombatPlaybackSpeedDetour
{
    private const string TargetMethod = "SetSpeed";
    private const int TargetParameterCount = 2;

    // The game reads the log flag as one byte.
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void SetSpeedNative(IntPtr receiver, float speed, byte log, IntPtr method);

    // Static, so the delegates the native code calls stay alive for the process.
    private static readonly SetSpeedNative Replacement = OnSetSpeed;
    private static IDetour? _detour;
    private static SetSpeedNative? _original;
    private static long _hits;
    private static long _overrides;

    public static BppLogField[] Summary() =>
        [
            ("combat_speed_hits", Interlocked.Read(ref _hits)),
            ("combat_speed_overrides", Interlocked.Read(ref _overrides)),
        ];

    public static void Install()
    {
        if (_detour != null)
            return;
        var method = IL2CPP.il2cpp_class_get_method_from_name(
            Il2CppClassPointerStore<CombatSimHandler>.NativeClassPtr,
            TargetMethod,
            TargetParameterCount
        );
        if (method == IntPtr.Zero)
            throw new MissingMethodException(nameof(CombatSimHandler), TargetMethod);
        var entry = UnityVersionHandler.Wrap((Il2CppMethodInfo*)method).MethodPointer;
        if (entry == IntPtr.Zero)
            throw new InvalidOperationException("CombatSimHandler.SetSpeed has no native body.");

        var detour = Il2CppInteropRuntime.Instance.DetourProvider.Create(entry, Replacement);
        detour.Apply();
        _original =
            detour.GenerateTrampoline<SetSpeedNative>()
            ?? throw new InvalidOperationException(
                "The CombatSimHandler.SetSpeed detour has no trampoline."
            );
        _detour = detour;
    }

    // No managed exception may escape this native callback.
    private static void OnSetSpeed(IntPtr receiver, float speed, byte log, IntPtr method)
    {
        try
        {
            Interlocked.Increment(ref _hits);
            var state = AndroidPatchHost.Features?.CombatStatus?.State;
            if (state?.ShouldOverrideCombatSpeed(speed) == true)
            {
                speed = state.CombatSpeedMultiplier;
                Interlocked.Increment(ref _overrides);
            }
        }
        catch (Exception exception)
        {
            AndroidPatchHost.ReportProbeError("combat_speed", exception);
        }
        _original!(receiver, speed, log, method);
    }
}
