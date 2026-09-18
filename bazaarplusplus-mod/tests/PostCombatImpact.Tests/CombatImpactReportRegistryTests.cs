#nullable enable
using BazaarPlusPlus.Game.PostCombatImpact.Data;
using Xunit;

namespace PostCombatImpact.Tests;

public sealed class CombatImpactReportRegistryTests
{
    [Fact]
    public void New_generation_clears_the_previous_report_and_rejects_stale_publication()
    {
        var registry = new CombatImpactReportRegistry();
        var firstGeneration = registry.BeginGeneration();
        var firstReport = Report("first");
        Assert.True(registry.TryPublish(firstGeneration, firstReport));
        Assert.Same(firstReport, registry.Latest);

        var secondGeneration = registry.BeginGeneration();

        Assert.Same(CombatImpactReport.Empty, registry.Latest);
        Assert.False(registry.TryPublish(firstGeneration, Report("stale")));
        Assert.Same(CombatImpactReport.Empty, registry.Latest);

        var secondReport = Report("second");
        Assert.True(registry.TryPublish(secondGeneration, secondReport));
        Assert.Same(secondReport, registry.Latest);
    }

    [Fact]
    public void Reset_invalidates_in_flight_projection()
    {
        var registry = new CombatImpactReportRegistry();
        var generation = registry.BeginGeneration();

        registry.Reset();

        Assert.False(registry.TryPublish(generation, Report("late")));
        Assert.Same(CombatImpactReport.Empty, registry.Latest);
    }

    private static CombatImpactReport Report(string id) =>
        new(
            [new CombatImpactSource(new CombatImpactEntity(id, id, "Item", null, 0), 0, 0, [])],
            []
        );
}
