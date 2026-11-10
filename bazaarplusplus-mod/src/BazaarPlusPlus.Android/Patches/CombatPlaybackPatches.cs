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

/// <summary>
/// The combat speed override on <c>CombatSimHandler.SetSpeed</c>, as a native detour and not a
/// Harmony patch. The native method never reads its receiver, so its call sites in the combat
/// loop load none: the receiver register holds what the call before left there. That is 0 after
/// <c>ShouldFastForward</c> returned false, 1 after it returned true, and the handler's class
/// pointer at the reset for a fight's last frames. A Harmony patch wraps a non-zero receiver as
/// an IL2CPP object before any prefix runs, so with the game's Fast Forward First Fight setting
/// on, it read address 1 and the game stopped in the first Day 1 fight. This detour takes the
/// receiver and the method pointer as opaque values, changes only the speed, and passes all of
/// them to the original.
/// </summary>
internal static unsafe class CombatPlaybackSpeedDetour
{
    private const string TargetMethod = "SetSpeed";
    private const int TargetParameterCount = 2;

    // The game's own call: receiver, speed, log flag, method pointer. The flag is one byte, as
    // the native method reads it.
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

    /// <summary>Installs the detour once; throws when the game has no such method.</summary>
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

        // The provider every Harmony patch of this host installs through.
        var detour = Il2CppInteropRuntime.Instance.DetourProvider.Create(entry, Replacement);
        detour.Apply();
        _original =
            detour.GenerateTrampoline<SetSpeedNative>()
            ?? throw new InvalidOperationException(
                "The CombatSimHandler.SetSpeed detour has no trampoline."
            );
        _detour = detour;
    }

    // The native code calls this, so no exception can leave it. The original's one call out,
    // the speed event, catches its listeners' exceptions itself.
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
