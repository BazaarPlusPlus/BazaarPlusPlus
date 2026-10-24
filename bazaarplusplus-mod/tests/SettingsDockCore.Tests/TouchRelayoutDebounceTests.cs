#nullable enable
using BazaarPlusPlus.Core.Ui;
using Xunit;

namespace SettingsDockCore.Tests;

/// <summary>
/// When an open touch panel lays itself out again (<see cref="TouchRelayoutDebounce"/>): only for
/// a screen key that has held still, not while a card is setting up, and never later than the
/// busy cap.
/// </summary>
public sealed class TouchRelayoutDebounceTests
{
    [Fact]
    public void The_laid_out_key_never_relays_out()
    {
        var debounce = new TouchRelayoutDebounce("a", stableFrames: 3);

        for (var frame = 0; frame < 10; frame++)
            Assert.False(debounce.Observe("a", settingUp: false));
        Assert.Equal(0, debounce.HeldFrames);
    }

    [Fact]
    public void A_new_key_relays_out_once_it_holds_for_the_stable_frames()
    {
        var debounce = new TouchRelayoutDebounce("a", stableFrames: 3);

        Assert.False(debounce.Observe("b", settingUp: false));
        Assert.False(debounce.Observe("b", settingUp: false));
        Assert.True(debounce.Observe("b", settingUp: false));
        Assert.Equal(2, debounce.HeldFrames);
    }

    [Fact]
    public void A_flickering_key_restarts_the_count()
    {
        var debounce = new TouchRelayoutDebounce("a", stableFrames: 3);

        Assert.False(debounce.Observe("b", settingUp: false));
        Assert.False(debounce.Observe("b", settingUp: false));
        Assert.False(debounce.Observe("c", settingUp: false));
        Assert.False(debounce.Observe("c", settingUp: false));
        Assert.True(debounce.Observe("c", settingUp: false));
    }

    [Fact]
    public void Returning_to_the_laid_out_key_cancels_the_pending_change()
    {
        var debounce = new TouchRelayoutDebounce("a", stableFrames: 3);

        Assert.False(debounce.Observe("b", settingUp: false));
        Assert.False(debounce.Observe("b", settingUp: false));
        Assert.False(debounce.Observe("a", settingUp: false));
        Assert.False(debounce.Observe("b", settingUp: false));
        Assert.False(debounce.Observe("b", settingUp: false));
        Assert.True(debounce.Observe("b", settingUp: false));
    }

    [Fact]
    public void A_card_in_setup_holds_a_stable_change_until_it_finishes()
    {
        var debounce = new TouchRelayoutDebounce("a", stableFrames: 2, maxBusyFrames: 50);

        Assert.False(debounce.Observe("b", settingUp: true));
        for (var frame = 0; frame < 5; frame++)
            Assert.False(debounce.Observe("b", settingUp: true));
        Assert.True(debounce.Observe("b", settingUp: false));
    }

    [Fact]
    public void A_setup_that_never_ends_holds_no_longer_than_the_busy_cap()
    {
        var debounce = new TouchRelayoutDebounce("a", stableFrames: 2, maxBusyFrames: 4);

        Assert.False(debounce.Observe("b", settingUp: true));
        for (var frame = 0; frame < 4; frame++)
            Assert.False(debounce.Observe("b", settingUp: true));
        Assert.True(debounce.Observe("b", settingUp: true));
    }

    [Fact]
    public void Laying_out_adopts_the_key_and_clears_the_pending_count()
    {
        var debounce = new TouchRelayoutDebounce("a", stableFrames: 3);
        Assert.False(debounce.Observe("b", settingUp: false));
        Assert.False(debounce.Observe("b", settingUp: false));

        debounce.LaidOutAt("b");

        Assert.Equal("b", debounce.LaidOut);
        Assert.False(debounce.Observe("b", settingUp: false));
        Assert.False(debounce.Observe("c", settingUp: false));
        Assert.False(debounce.Observe("c", settingUp: false));
        Assert.True(debounce.Observe("c", settingUp: false));
    }

    [Fact]
    public void A_panel_with_no_layout_yet_relays_out_for_its_first_stable_key()
    {
        var debounce = new TouchRelayoutDebounce(string.Empty, stableFrames: 2);

        Assert.False(debounce.Observe("a", settingUp: false));
        Assert.True(debounce.Observe("a", settingUp: false));
    }
}
