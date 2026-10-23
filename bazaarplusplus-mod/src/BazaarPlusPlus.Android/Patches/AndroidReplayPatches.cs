using BazaarPlusPlus.Android.CombatReplay;
using BazaarPlusPlus.Android.CombatReplay.Bootstrap;
using BazaarPlusPlus.Android.CombatReplay.PlaybackUi;
using BazaarPlusPlus.Infrastructure.Logging;
using HarmonyLib;
using Il2Cpp;
using Il2CppTheBazaar;
using Il2CppTheBazaar.Game.CardFrames;
using Il2CppTheBazaar.UI.EncounterPicker;

namespace BazaarPlusPlus.Android.Patches;

/// <summary>
/// The hit counts of the replay seams, for the hooks summary. A small IL2CPP method can be
/// inlined at its native call sites and never reach a detour that installed cleanly, so a device
/// run proves each seam by its count, not by its install line.
/// </summary>
internal static class AndroidReplayPatchCounters
{
    private static long _exitHits;
    private static long _exitsHandled;
    private static long _finalizeSkips;
    private static long _readinessTracked;
    private static long _visualSkips;

    public static long ExitHit() => Interlocked.Increment(ref _exitHits);

    public static void ExitHandled() => Interlocked.Increment(ref _exitsHandled);

    public static void FinalizeSkipped() => Interlocked.Increment(ref _finalizeSkips);

    public static void ReadinessTracked() => Interlocked.Increment(ref _readinessTracked);

    public static void VisualSkipped() => Interlocked.Increment(ref _visualSkips);

    public static BppLogField[] Summary() =>
        [
            ("replay_exit_hits", Interlocked.Read(ref _exitHits)),
            ("replay_exits_handled", Interlocked.Read(ref _exitsHandled)),
            ("replay_finalize_skips", Interlocked.Read(ref _finalizeSkips)),
            ("replay_readiness_tracked", Interlocked.Read(ref _readinessTracked)),
            ("replay_visual_skips", Interlocked.Read(ref _visualSkips)),
        ];

    /// <summary>A replay is starting or playing; the seams change nothing otherwise.</summary>
    public static bool ReplayActive =>
        AndroidPatchHost.Mode == AndroidPatchMode.Enabled && AndroidReplayGuard.IsActive;
}

/// <summary>
/// The bootstrapped saved replay's exit (ADR-0002): the game's <c>ReplayState.Exit</c> routes into
/// the runtime, which returns to the menu itself; the original body runs whenever the runtime
/// does not own the exit.
/// </summary>
[HarmonyPatch(typeof(ReplayState), nameof(ReplayState.Exit))]
internal static class AndroidReplayStateExitPatch
{
    [HarmonyPrefix]
    private static bool Prefix()
    {
        AndroidReplayPatchCounters.ExitHit();
        if (AndroidPatchHost.Mode != AndroidPatchMode.Enabled)
            return true;
        try
        {
            if (!AndroidCombatReplayRuntime.TryExitBootstrappedSavedReplayToMenu())
                return true;
            AndroidReplayPatchCounters.ExitHandled();
            return false;
        }
        catch (Exception exception)
        {
            AndroidPatchHost.ReportProbeError("replay_exit", exception);
            return true;
        }
    }
}

/// <summary>
/// The run initialization a lobby bootstrap would otherwise run against the live server: while a
/// replay starts or plays, <c>StartRunAppState.FinalizeRunInitialization</c> returns the game's own
/// completed task instead, the desktop's <c>CombatReplayFinalizeRunInitializationPatch</c> on IL2CPP.
/// </summary>
[HarmonyPatch(typeof(StartRunAppState), nameof(StartRunAppState.FinalizeRunInitialization))]
internal static class AndroidReplayFinalizeRunInitializationPatch
{
    [HarmonyPrefix]
    private static bool Prefix(ref Il2CppSystem.Threading.Tasks.Task __result)
    {
        if (!AndroidReplayPatchCounters.ReplayActive)
            return true;
        __result = Il2CppSystem.Threading.Tasks.Task.CompletedTask;
        AndroidReplayPatchCounters.FinalizeSkipped();
        return false;
    }
}

/// <summary>Records each item setup task while a replay injects; the task is never replaced.</summary>
[HarmonyPatch(typeof(ItemController), nameof(ItemController.Setup))]
internal static class AndroidReplayItemSetupReadinessPatch
{
    [HarmonyPostfix]
    private static void Postfix(Il2CppSystem.Threading.Tasks.Task __result)
    {
        var tasks = AndroidReplayPresentation.Tasks;
        if (!tasks.IsTracking || __result == null)
            return;
        tasks.Track(__result);
        AndroidReplayPatchCounters.ReadinessTracked();
    }
}

/// <summary>Records the replay board's spawn task, the parameterless overload only.</summary>
[HarmonyPatch(typeof(ReplayState), nameof(ReplayState.SpawnCombatCards), new Type[0])]
internal static class AndroidReplayBoardSpawnReadinessPatch
{
    [HarmonyPostfix]
    private static void Postfix(Il2CppSystem.Threading.Tasks.Task __result)
    {
        var tasks = AndroidReplayPresentation.Tasks;
        if (!tasks.IsTracking || __result == null)
            return;
        tasks.Track(__result);
        AndroidReplayPatchCounters.ReadinessTracked();
    }
}

/// <summary>The lobby's encounter picker map stays hidden under a replay.</summary>
[HarmonyPatch(typeof(EncounterPickerMapController), nameof(EncounterPickerMapController.Show))]
internal static class AndroidReplayEncounterPickerShowPatch
{
    [HarmonyPrefix]
    private static bool Prefix(EncounterPickerMapController __instance)
    {
        if (!AndroidReplayPatchCounters.ReplayActive)
            return true;
        AndroidReplayPatchCounters.VisualSkipped();
        AndroidReplayPlaybackUi.HideEncounterPickerOverlays();
        if (__instance != null)
            __instance.gameObject.SetActive(false);
        return false;
    }
}

[HarmonyPatch(
    typeof(InjectedEncounterPickerMapController),
    nameof(InjectedEncounterPickerMapController.PlayEnter)
)]
internal static class AndroidReplayInjectedEncounterPickerPatch
{
    [HarmonyPrefix]
    private static bool Prefix(InjectedEncounterPickerMapController __instance)
    {
        if (!AndroidReplayPatchCounters.ReplayActive)
            return true;
        AndroidReplayPatchCounters.VisualSkipped();
        AndroidReplayPlaybackUi.HideEncounterPickerOverlays();
        if (__instance != null)
            __instance.gameObject.SetActive(false);
        return false;
    }
}

[HarmonyPatch(
    typeof(ClockInjectedEncounterController),
    nameof(ClockInjectedEncounterController.SpawnEncounterPickerVFX)
)]
internal static class AndroidReplayClockEncounterPickerPatch
{
    [HarmonyPrefix]
    private static bool Prefix()
    {
        if (!AndroidReplayPatchCounters.ReplayActive)
            return true;
        AndroidReplayPatchCounters.VisualSkipped();
        AndroidReplayPlaybackUi.HideEncounterPickerOverlays();
        return false;
    }
}

/// <summary>A replayed board's price tags do not replay their change effect.</summary>
[HarmonyPatch(typeof(PriceTagContainer), nameof(PriceTagContainer.PlayPriceChangeVFX))]
internal static class AndroidReplayPriceTagVfxPatch
{
    [HarmonyPrefix]
    private static bool Prefix()
    {
        if (!AndroidReplayPatchCounters.ReplayActive)
            return true;
        AndroidReplayPatchCounters.VisualSkipped();
        return false;
    }
}
