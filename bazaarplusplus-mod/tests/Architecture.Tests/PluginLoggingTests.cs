#nullable enable
using System.Reflection;
using BazaarPlusPlus.GameInterop.Fonts;
using BazaarPlusPlus.Infrastructure.Logging;
using Xunit;

namespace BazaarPlusPlus;

public sealed class PluginLoggingTests
{
    [Fact]
    public void Plugin_events_match_the_locked_E01_E06_E08_E09_manifest()
    {
        var actual = Definitions().ToDictionary(x => x.EventId, Describe, StringComparer.Ordinal);
        var expected = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["plugin.initialization.succeeded"] =
                "plugin_version:High:None|game_build:High:None|build_channel:Low:None",
            ["plugin.game_build.degraded"] =
                "game_build:High:None|build_channel:Low:None|reason_code:Low:None",
            ["plugin.initialization.failed"] = "phase:Low:None|reason_code:Low:None",
            ["plugin.shutdown.degraded"] =
                "failed_step_count:Low:None|first_failed_step:Low:None|reason_code:Low:None",
            ["plugin.online_services.degraded"] = "reason_code:Low:None|endpoint:Low:None",
            ["plugin.patch.apply_failed"] = "patch_type:High:None|reason_code:Low:None",
            ["plugin.patches.degraded"] = "failed_patch_count:Low:None|reason_code:Low:None",
            ["plugin.event_handler.degraded"] =
                "event_id:Low:None|handler_id:Low:None|reason_code:Low:None",
            ["plugin.feature_start.degraded"] = "feature:Low:None|reason_code:Low:None",
            ["plugin.feature_stop.degraded"] = "feature:Low:None|reason_code:Low:None",
            ["plugin.native_game_fonts.degraded"] = "stage:Low:None|reason_code:Low:None",
            ["plugin.native_game_fonts.loaded"] =
                "font_count:High:None|font_names:High:None|source_font:High:None",
            ["plugin.native_game_fonts.recovered"] = "font_count:High:None",
            ["plugin.native_game_fonts.cleanup_failed"] = "stage:Low:None",
            ["plugin.native_game_fonts.text_rejected"] = "surface:Low:None|code_point:High:None",
        };

        Assert.Equal(expected.Count, actual.Count);
        foreach (var pair in expected)
            Assert.Equal(pair.Value, actual[pair.Key]);
        Assert.All(
            Definitions(),
            definition => Assert.Same(BppLogFeatureScope.Plugin, definition.Scope)
        );
        var validation = BppLogEventCatalog.FromDefinitions(Definitions().ToArray()).Validate();
        Assert.True(validation.IsValid, string.Join("; ", validation.Violations));
    }

    [Fact]
    public void Warning_storm_keys_use_only_declared_low_cardinality_fields()
    {
        AssertStorm(PluginLogEvents.GameBuildDegraded, "reason_code");
        AssertStorm(PluginLogEvents.ShutdownDegraded, "reason_code");
        AssertStorm(PluginLogEvents.OnlineServicesDegraded, "endpoint", "reason_code");
        AssertStorm(PluginLogEvents.PatchesDegraded, "reason_code");
        AssertStorm(PluginLogEvents.EventHandlerDegraded, "event_id", "handler_id");
        AssertStorm(PluginLogEvents.FeatureStartDegraded, "feature", "reason_code");
        AssertStorm(PluginLogEvents.FeatureStopDegraded, "feature", "reason_code");
        AssertStorm(NativeGameFontsLogEvents.Degraded, "stage", "reason_code");
        AssertStorm(NativeGameFontsLogEvents.TextRejected, "surface");
    }

    [Fact]
    public void Teardown_runs_every_step_and_returns_one_aggregate_failure()
    {
        var calls = new List<PluginTeardownStep>();
        var accumulator = new PluginTeardownAccumulator();

        accumulator.Run(
            PluginTeardownStep.UnpatchHarmony,
            () =>
            {
                calls.Add(PluginTeardownStep.UnpatchHarmony);
                throw new InvalidOperationException("first");
            }
        );
        accumulator.Run(
            PluginTeardownStep.UnmountComponents,
            () =>
            {
                calls.Add(PluginTeardownStep.UnmountComponents);
                throw new ArgumentException("second");
            }
        );
        accumulator.Run(
            PluginTeardownStep.DisposeComposition,
            () => calls.Add(PluginTeardownStep.DisposeComposition)
        );

        Assert.Equal(
            [
                PluginTeardownStep.UnpatchHarmony,
                PluginTeardownStep.UnmountComponents,
                PluginTeardownStep.DisposeComposition,
            ],
            calls
        );
        Assert.Equal(2, accumulator.FailedStepCount);
        Assert.Equal(PluginTeardownStep.UnpatchHarmony, accumulator.FirstFailedStep);
        Assert.IsType<InvalidOperationException>(accumulator.FirstException);
    }

    [Fact]
    public void Runtime_types_map_to_closed_plugin_event_handler_and_feature_ids()
    {
        Assert.Equal(
            PluginEventId.RunLifecycleChanged,
            PluginLogIdentity.EventId("RunLifecycleChanged")
        );
        Assert.Equal(PluginEventId.Unknown, PluginLogIdentity.EventId("ThirdPartyEvent"));

        Assert.Equal(
            PluginHandlerId.RunLoggingModule,
            PluginLogIdentity.HandlerId("BazaarPlusPlus.Game.RunLogging.RunLoggingModule+<>c")
        );
        Assert.Equal(
            PluginHandlerId.Unknown,
            PluginLogIdentity.HandlerId("ThirdParty.DynamicHandler")
        );

        var features = new Dictionary<string, PluginFeatureId>(StringComparer.Ordinal)
        {
            ["BazaarPlusPlus.Game.RunLifecycle.RunLifecycleModule"] = PluginFeatureId.RunLifecycle,
            ["BazaarPlusPlus.Game.CombatReplay.CombatReplayModule"] = PluginFeatureId.CombatReplay,
            ["BazaarPlusPlus.Game.CombatStatusBar.CombatStatusBarModule"] =
                PluginFeatureId.CombatStatusBar,
            ["BazaarPlusPlus.GameInterop.VoiceSubtitles.VoiceSubtitlesInteropModule"] =
                PluginFeatureId.VoiceSubtitlesInterop,
            ["BazaarPlusPlus.Game.VoiceSubtitles.VoiceSubtitlesModule"] =
                PluginFeatureId.VoiceSubtitles,
            ["BazaarPlusPlus.Game.PostCombatImpact.PostCombatImpactModule"] =
                PluginFeatureId.PostCombatImpact,
            ["BazaarPlusPlus.Game.Supporters.SupporterCatalogModule"] = PluginFeatureId.Supporters,
            ["BazaarPlusPlus.Game.RunLogging.RunLoggingModule"] = PluginFeatureId.RunLogging,
            ["BazaarPlusPlus.Game.BundlePipeline.BundleSealCoordinator"] =
                PluginFeatureId.BundleSeal,
        };
        foreach (var pair in features)
            Assert.Equal(pair.Value, PluginLogIdentity.FeatureId(pair.Key));
        Assert.Equal(PluginFeatureId.Unknown, PluginLogIdentity.FeatureId("ThirdParty.Feature"));
    }

    private static IEnumerable<BppLogEventDefinition> Definitions() =>
        new[] { typeof(PluginLogEvents), typeof(NativeGameFontsLogEvents) }
            .SelectMany(type =>
                type.GetFields(
                    BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.DeclaredOnly
                )
            )
            .Where(field => field.FieldType == typeof(BppLogEventDefinition))
            .Select(field => (BppLogEventDefinition)field.GetValue(null)!);

    private static string Describe(BppLogEventDefinition definition) =>
        string.Join(
            "|",
            definition.Fields.Select(field =>
                $"{field.Name}:{field.Cardinality}:{field.Correlation}"
            )
        );

    private static void AssertStorm(BppLogEventDefinition definition, params string[] names)
    {
        var policy = Assert.IsType<BppLogStormPolicy>(definition.StormPolicy);
        Assert.Equal(names, policy.KeyFields.Select(field => field.Name));
        Assert.All(
            policy.KeyFields,
            field => Assert.Equal(BppLogCardinality.Low, field.Cardinality)
        );
    }
}
