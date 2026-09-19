#nullable enable
using BazaarPlusPlus.Game.CollectionPanel;
using Xunit;

namespace CollectionPanelLogging.Tests;

public sealed class CollectionPortraitFailureGateTests
{
    [Fact]
    public void Cached_failure_reports_once_until_outcome_changes_or_recovers()
    {
        var gate = new CollectionPortraitFailureGate<string, string>();

        Assert.True(gate.ShouldReport("portrait", "missing"));
        Assert.False(gate.ShouldReport("portrait", "missing"));
        Assert.True(gate.ShouldReport("portrait", "load_exception"));
        Assert.False(gate.ShouldReport("portrait", "load_exception"));

        gate.Clear("portrait");
        Assert.True(gate.ShouldReport("portrait", "missing"));
    }

    [Fact]
    public void Gate_is_bounded_and_keeps_recent_negative_cache_entries()
    {
        var gate = new CollectionPortraitFailureGate<int, string>(maximumEntries: 2);

        Assert.True(gate.ShouldReport(1, "missing"));
        Assert.True(gate.ShouldReport(2, "missing"));
        Assert.False(gate.ShouldReport(1, "missing"));
        Assert.True(gate.ShouldReport(3, "missing"));

        Assert.Equal(2, gate.Count);
        Assert.False(gate.ShouldReport(1, "missing"));
        Assert.True(gate.ShouldReport(2, "missing"));
    }
}
