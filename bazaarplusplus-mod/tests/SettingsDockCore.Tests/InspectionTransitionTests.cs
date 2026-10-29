using BazaarPlusPlus.Core.Ui;
using Xunit;

namespace SettingsDockCore.Tests;

public sealed class InspectionTransitionTests
{
    [Fact]
    public void Loading_does_not_spend_the_entrance_duration()
    {
        var transition = new InspectionTransition();
        transition.Begin(true);
        transition.Tick(10f);
        Assert.Equal(0f, transition.Progress);
        transition.Ready();
        transition.Tick(0.16f);
        Assert.InRange(transition.Progress, 0.45f, 0.55f);
        transition.Tick(0.16f);
        Assert.Equal(1f, transition.Progress);
        Assert.False(transition.Finished);
    }

    [Fact]
    public void Back_mid_entrance_reverses_without_jumping_and_repeated_back_does_not_restart_exit()
    {
        var transition = new InspectionTransition();
        transition.Begin(true);
        transition.Ready();
        transition.Tick(0.08f);
        var before = transition.Progress;
        transition.Dismiss();
        Assert.Equal(before, transition.Progress);
        transition.Tick(0.09f);
        Assert.InRange(transition.Progress, 0f, before);
        transition.Dismiss();
        transition.Tick(0.12f);
        Assert.True(transition.Finished);
        Assert.Equal(0f, transition.Progress);
    }

    [Fact]
    public void Motion_starts_and_finishes_gently_instead_of_spending_its_distance_on_the_first_frame()
    {
        var transition = new InspectionTransition();
        transition.Begin(true);
        transition.Ready();
        transition.Tick(1f / 60f);
        var startDistance = transition.Progress;
        Assert.InRange(startDistance, 0f, 0.02f);
        transition.Tick(0.16f - 1f / 60f);
        var midpoint = transition.Progress;
        transition.Tick(1f / 60f);
        Assert.True(transition.Progress - midpoint > startDistance * 4f);
        transition.Tick(0.32f - 0.16f - 2f / 60f);
        Assert.InRange(1f - transition.Progress, 0f, 0.02f);
    }

    [Fact]
    public void Back_while_loading_cannot_be_resurrected_by_a_late_ready()
    {
        var transition = new InspectionTransition();
        transition.Begin(true);
        transition.Dismiss();
        transition.Ready();
        transition.Tick(1f);
        Assert.True(transition.Finished);
        Assert.Equal(0f, transition.Progress);
    }

    [Fact]
    public void Remove_animations_still_waits_for_readiness_then_opens_and_closes_immediately()
    {
        var transition = new InspectionTransition();
        transition.Begin(false);
        Assert.True(transition.Pending);
        transition.Ready();
        Assert.Equal(1f, transition.Progress);
        transition.Dismiss();
        Assert.True(transition.Finished);
        transition.Begin(true);
        Assert.False(transition.Finished);
        Assert.False(transition.Closing);
    }
}
