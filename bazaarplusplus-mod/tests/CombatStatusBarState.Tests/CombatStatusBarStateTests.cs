using System.Reflection;
using BazaarPlusPlus.Game.CombatStatusBar;
using BazaarPlusPlus.Game.ItemEnchantPreview;
using BazaarPlusPlus.Game.NameOverride;
using BazaarPlusPlus.Game.Settings;
using Xunit;

namespace BazaarPlusPlus.Tests;

public sealed class CombatStatusBarStateTests : IDisposable
{
    public CombatStatusBarStateTests()
    {
        CombatStatusBar.ResetStateForTests();
    }

    public void Dispose()
    {
        CombatStatusBar.ResetStateForTests();
        BazaarPlusPlus.Core.Runtime.TestServices.Instance.RunContext.IsInGameRun = false;
        BazaarPlusPlus.Core.Runtime.TestServices.Instance.GameStateProbe.Result = false;
    }

    [Fact]
    public void GetDisplayedTimeText_ReturnsZeroTimeOutsideCombat()
    {
        var result = CombatStatusBar.GetDisplayedTimeText();

        Assert.Equal("0:00.00", result);
    }

    [Fact]
    public void GetDisplayedTimeText_ReturnsLogicalCombatTimeDuringCombat()
    {
        CombatStatusBar.BeginCombatPlayback();
        for (var i = 0; i < 25; i++)
            CombatStatusBar.AdvanceCombatFrame();

        var result = CombatStatusBar.GetDisplayedTimeText();

        Assert.Equal("0:01.20", result);
    }

    [Fact]
    public void GetDisplayedTimeText_ReturnsLastCombatTimeAfterCombatEnds()
    {
        CombatStatusBar.BeginCombatPlayback();
        for (var i = 0; i < 25; i++)
            CombatStatusBar.AdvanceCombatFrame();

        CombatStatusBar.EndCombatPlayback();

        Assert.Equal("0:01.20", CombatStatusBar.GetDisplayedTimeText());
    }

    [Fact]
    public void FrameDisplayText_IsNotPartOfCombatStatusBarState()
    {
        var frameTextMethod = typeof(CombatStatusBar).GetMethod(
            "GetDisplayedFrameText",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        Assert.Null(frameTextMethod);
    }

    [Fact]
    public void DisplayedTimeText_StillUsesLogicalFrameIndex()
    {
        CombatStatusBar.BeginCombatPlayback();

        CombatStatusBar.AdvanceCombatFrame();
        Assert.Equal("0:00.00", CombatStatusBar.GetDisplayedTimeText());

        CombatStatusBar.AdvanceCombatFrame();
        Assert.Equal("0:00.05", CombatStatusBar.GetDisplayedTimeText());
    }

    [Fact]
    public void CombatSpeed_PlaqueCyclesThroughEverySupportedStepAndWraps()
    {
        Assert.Equal(0.5f, CombatStatusBar.CycleCombatSpeed(), 3);
        Assert.Equal(0.67f, CombatStatusBar.CycleCombatSpeed(), 3);
        Assert.Equal(1f, CombatStatusBar.CycleCombatSpeed(), 3);
        Assert.Equal(0.5f, CombatStatusBar.CycleCombatSpeed(), 3);
    }

    [Fact]
    public void CombatSpeed_UsesRequestedDiscreteSteps()
    {
        Assert.Equal(1f, CombatStatusBar.CombatSpeedMultiplier, 3);
        Assert.Equal(1f, CombatStatusBar.NormalizeConfiguredDefaultSpeed(1f), 3);
        Assert.Equal(1f, CombatStatusBar.NormalizeConfiguredDefaultSpeed(1.25f), 3);

        CombatStatusBar.SetCombatSpeed(0.67f);
        Assert.Equal(0.67f, CombatStatusBar.CombatSpeedMultiplier, 3);
    }

    [Fact]
    public void CombatSpeed_OnlyOverridesDuringPlayback()
    {
        Assert.False(CombatStatusBar.ShouldOverrideCombatSpeed(1f));

        CombatStatusBar.SetCombatSpeed(0.5f);

        CombatStatusBar.BeginCombatPlayback();

        Assert.True(CombatStatusBar.ShouldOverrideCombatSpeed(1f));
        Assert.False(CombatStatusBar.ShouldOverrideCombatSpeed(1.2f));

        CombatStatusBar.SetCombatSpeed(1f);
    }

    [Fact]
    public void ShouldRenderForState_RequiresInGameRun()
    {
        BazaarPlusPlus.Core.Runtime.TestServices.Instance.RunContext.IsInGameRun = false;
        Assert.False(CombatStatusBar.ShouldRenderForState());

        BazaarPlusPlus.Core.Runtime.TestServices.Instance.RunContext.IsInGameRun = true;
        Assert.True(CombatStatusBar.ShouldRenderForState());
    }

    [Fact]
    public void ShouldRenderForState_ShowsDuringReplay_WhenProbeReportsInGameRun()
    {
        var services = BazaarPlusPlus.Core.Runtime.TestServices.Instance;

        // Replay's start guard forces the cached flag to false, and entering ReplayState never
        // fires RunStarted, so the cache stays false throughout playback...
        services.RunContext.IsInGameRun = false;
        // ...but the live probe reports ReplayState as in-game-run.
        services.GameStateProbe.Result = true;

        Assert.True(CombatStatusBar.ShouldRenderForState());
    }

    [Fact]
    public void DockDefinition_ActionRow_InvokesAction_AndTracksActiveState()
    {
        var open = false;
        var activationCount = 0;
        var definition = new BppSettingsDockDefinition(
            "GameHistory",
            _ => "Game History",
            () => open,
            () =>
            {
                activationCount++;
                open = true;
            },
            collapseAfterActivate: true
        );

        Assert.False(definition.IsActive());

        definition.Activate!();

        Assert.Equal(1, activationCount);
        Assert.True(definition.IsActive());
        Assert.True(definition.CollapseAfterActivate);
    }

    [Theory]
    [InlineData("zh-Hans", "匿名模式")]
    [InlineData("zh-CN", "匿名模式")]
    [InlineData("en", "Anonymous Mode")]
    [InlineData("", "Anonymous Mode")]
    public void NameOverrideSettingsMenuLabel_UsesChineseOnlyForSimplifiedChinese(
        string languageCode,
        string expected
    )
    {
        LocalizationTestHost.Install(languageCode);
        var result = NameOverrideSettingsMenuLabel.Resolve(languageCode);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("zh-Hans", "附魔预览")]
    [InlineData("zh-CN", "附魔预览")]
    [InlineData("en", "Enchant Preview")]
    [InlineData("", "Enchant Preview")]
    public void EnchantPreviewSettingsMenuLabel_UsesChineseOnlyForSimplifiedChinese(
        string languageCode,
        string expected
    )
    {
        LocalizationTestHost.Install(languageCode);
        var result = EnchantPreviewSettingsMenuLabel.Resolve(languageCode);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(0.0f, true, 0.10f, 0.50f)]
    [InlineData(0.90f, true, 0.10f, 1.00f)]
    [InlineData(0.10f, false, 0.10f, 0.00f)]
    public void AdvanceVisualBlend_ApproachesTargetWithoutOvershooting(
        float current,
        bool active,
        float deltaTime,
        float expected
    )
    {
        var result = CombatStatusBar.AdvanceVisualBlend(current, active, deltaTime);

        Assert.Equal(expected, result, precision: 3);
    }
}
