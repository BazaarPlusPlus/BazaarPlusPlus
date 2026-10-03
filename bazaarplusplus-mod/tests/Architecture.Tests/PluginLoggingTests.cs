#nullable enable
using Xunit;

namespace BazaarPlusPlus;

public sealed class PluginLoggingTests
{
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
}
