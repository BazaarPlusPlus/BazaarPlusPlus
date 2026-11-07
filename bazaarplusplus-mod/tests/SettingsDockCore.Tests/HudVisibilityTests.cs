using BazaarPlusPlus.Core.Ui;
using Xunit;

namespace SettingsDockCore.Tests;

public sealed class HudVisibilityTests
{
    [Theory]
    [InlineData(true, false, false, false)]
    [InlineData(false, true, false, false)]
    [InlineData(false, false, true, false)]
    [InlineData(false, false, false, true)]
    public void Native_obstructions_hide_touch_controls_and_clearing_them_restores_controls(
        bool popup,
        bool inspection,
        bool endOfRun,
        bool changingScene
    )
    {
        var native = new NativeHudState(popup, inspection, endOfRun, changingScene);

        Assert.False(HudVisibility.TouchShortcuts(native, false, false, true));
        Assert.False(HudVisibility.TouchCombatClock(native, false, true));
        Assert.True(HudVisibility.TouchShortcuts(default, false, false, true));
        Assert.True(HudVisibility.TouchCombatClock(default, false, true));
    }

    [Fact]
    public void An_open_panel_hides_both_touch_controls()
    {
        Assert.False(HudVisibility.TouchShortcuts(default, true, false, true));
        Assert.False(HudVisibility.TouchCombatClock(default, true, true));
    }

    [Fact]
    public void Playing_hides_shortcuts_but_leaves_an_eligible_clock_visible()
    {
        Assert.False(HudVisibility.TouchShortcuts(default, false, true, true));
        Assert.True(HudVisibility.TouchCombatClock(default, false, true));
    }

    [Fact]
    public void Missing_anchor_and_clock_ineligibility_are_independent()
    {
        Assert.False(HudVisibility.TouchShortcuts(default, false, false, false));
        Assert.True(HudVisibility.TouchCombatClock(default, false, true));
        Assert.True(HudVisibility.TouchShortcuts(default, false, false, true));
        Assert.False(HudVisibility.TouchCombatClock(default, false, false));
    }

    [Theory]
    [InlineData(true, false, true, true)]
    [InlineData(false, true, true, true)]
    [InlineData(false, false, false, true)]
    [InlineData(false, false, true, false)]
    public void Desktop_dock_requires_enabled_available_layout_without_suppression(
        bool endOfRun,
        bool screenshot,
        bool enabled,
        bool layout
    ) => Assert.False(HudVisibility.DesktopDockButton(endOfRun, screenshot, enabled, layout));

    [Fact]
    public void Desktop_buttons_keep_their_own_setting_and_layout()
    {
        Assert.True(HudVisibility.DesktopDockButton(false, false, true, true));
        Assert.False(HudVisibility.DesktopDockButton(false, false, false, true));
        Assert.False(HudVisibility.DesktopDockButton(false, false, true, false));
    }
}
