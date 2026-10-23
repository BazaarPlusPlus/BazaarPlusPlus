using System.Collections.Concurrent;
using BazaarPlusPlus.Android.CombatReplay;
using BazaarPlusPlus.Android.GameInterop;
using BazaarPlusPlus.Android.GameInterop.CardRental;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using HarmonyLib;

namespace BazaarPlusPlus.Android.Patches;

/// <summary>
/// What the Android patches reach: the startup mode, the plugin version they render, the record
/// chain's composition, and the hit counters the periodic summary reports. Patches are static, so
/// this is static too, the Android counterpart of the desktop's BppPatchHost.
/// </summary>
internal static class AndroidPatchHost
{
    private const int LoggedProbeErrors = 5;
    private static readonly ConcurrentDictionary<string, long> MessageCounts = new(
        StringComparer.Ordinal
    );
    private static long _versionLabelHits;
    private static long _combatSimHits;
    private static long _tooltipHits;
    private static long _tooltipHiddenHits;
    private static long _tooltipRenderHits;
    private static long _runInitializedHits;
    private static int _probeErrors;

    public static AndroidPatchMode Mode { get; private set; } = AndroidPatchMode.Disabled;

    public static string PluginVersion { get; private set; } = string.Empty;

    /// <summary>The record chain, or null when it failed to start; hooks then only count.</summary>
    public static AndroidComposition? Composition { get; private set; }

    /// <summary>Applies every patch class on its own: a failing class degrades only itself.</summary>
    public static void Install(
        HarmonyLib.Harmony harmony,
        AndroidPatchMode mode,
        string version,
        AndroidComposition? composition
    )
    {
        Mode = mode;
        PluginVersion = version;
        Composition = composition;
        if (mode == AndroidPatchMode.Disabled)
            return;

        foreach (var type in AccessTools.GetTypesFromAssembly(typeof(AndroidPatchHost).Assembly))
        {
            try
            {
                var targets = harmony.CreateClassProcessor(type).Patch();
                if (targets is { Count: > 0 })
                    BppLog.InfoEvent(
                        new BppLogEvent(BppLogFeatureScope.Android, "android.patch.installed"),
                        ("patch_type", type.FullName),
                        ("targets", string.Join(",", targets.Select(Describe)))
                    );
            }
            catch (Exception exception)
            {
                BppLog.ErrorEvent(
                    new BppLogEvent(BppLogFeatureScope.Android, "android.patch.failed"),
                    exception,
                    ("patch_type", type.FullName)
                );
            }
        }
    }

    public static long CountVersionLabel() => Interlocked.Increment(ref _versionLabelHits);

    public static long CountCombatSim() => Interlocked.Increment(ref _combatSimHits);

    public static long CountTooltip() => Interlocked.Increment(ref _tooltipHits);

    public static long CountTooltipHidden() => Interlocked.Increment(ref _tooltipHiddenHits);

    public static long CountTooltipRender() => Interlocked.Increment(ref _tooltipRenderHits);

    public static long CountRunInitialized() => Interlocked.Increment(ref _runInitializedHits);

    public static long CountMessage(string nativeType) =>
        MessageCounts.AddOrUpdate(nativeType, 1, (_, previous) => previous + 1);

    public static void ReportProbeError(string probe, Exception exception)
    {
        if (Interlocked.Increment(ref _probeErrors) <= LoggedProbeErrors)
            BppLog.ErrorEvent(
                new BppLogEvent(BppLogFeatureScope.Android, "android.hook.failed"),
                exception,
                ("probe", probe)
            );
    }

    public static BppLogField[] Summary() =>
        [
            ("mode", Mode),
            ("version_label_hits", Interlocked.Read(ref _versionLabelHits)),
            ("combat_sim_hits", Interlocked.Read(ref _combatSimHits)),
            ("tooltip_hits", Interlocked.Read(ref _tooltipHits)),
            ("tooltip_render_hits", Interlocked.Read(ref _tooltipRenderHits)),
            ("tooltip_hidden_hits", Interlocked.Read(ref _tooltipHiddenHits)),
            ("upgrade_preview_entered", AndroidUpgradePreview.Entered),
            ("upgrade_preview_exited", AndroidUpgradePreview.Exited),
            ("upgrade_values_leased", AndroidUpgradePreviewValues.Leased),
            ("upgrade_values_returned", AndroidUpgradePreviewValues.Returned),
            ("upgrade_values_projected", AndroidUpgradePreviewValues.Projected),
            ("enchant_preview_rendered", AndroidEnchantPreview.Rendered),
            .. AndroidCardRental.Summary(),
            ("run_initialized_hits", Interlocked.Read(ref _runInitializedHits)),
            .. Composition?.RunLifecycleSource.Summary() ?? [],
            ("pvp_battles_recorded", Composition?.BattleCapture.Recorded ?? 0),
            ("replays_saved", Composition?.BattleCapture.ReplaysSaved ?? 0),
            ("replay_guard_messages", AndroidReplayGuard.IntakeMessages),
            ("replay_guard_capture_skips", AndroidReplayGuard.CaptureSkips),
            .. AndroidReplayPatchCounters.Summary(),
            (
                "messages",
                string.Join(
                    ",",
                    MessageCounts
                        .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                        .Select(pair => $"{pair.Key}:{pair.Value}")
                )
            ),
        ];

    private static string Describe(System.Reflection.MethodInfo method) =>
        $"{method.DeclaringType?.FullName}.{method.Name}";
}
