#nullable enable
using BazaarPlusPlus;
using Xunit;

namespace PureBehavior.Tests;

public sealed class PluginTeardownAccumulatorTests
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
}
