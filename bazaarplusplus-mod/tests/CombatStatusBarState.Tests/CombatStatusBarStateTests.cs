using System.Reflection;
using BazaarPlusPlus.Core.RunContext;
using BazaarPlusPlus.Game.CombatStatusBar;
using BazaarPlusPlus.Game.ItemEnchantPreview;
using BazaarPlusPlus.Game.NameOverride;
using BazaarPlusPlus.Game.Settings;
using Xunit;

namespace BazaarPlusPlus.Tests;

public sealed class CombatStatusBarStateTests
{
    private readonly TestPauseControl _pause = new();
    private readonly CombatStatusBarState _state;
    private readonly RunContextStore _runContext = new();
    private readonly TestGameStateProbe _probe = new();

    public CombatStatusBarStateTests()
    {
        _state = new CombatStatusBarState(_pause);
    }

    [Fact]
    public void GetDisplayedTimeText_ReturnsLogicalCombatTimeDuringCombat()
    {
        _state.BeginCombatPlayback();
        for (var i = 0; i < 25; i++)
            _state.AdvanceCombatFrame();

        var result = _state.GetDisplayedTimeText();

        Assert.Equal("0:01.20", result);
    }

    [Fact]
    public void GetDisplayedTimeText_ReturnsLastCombatTimeAfterCombatEnds()
    {
        _state.BeginCombatPlayback();
        for (var i = 0; i < 25; i++)
            _state.AdvanceCombatFrame();

        _state.EndCombatPlayback();

        Assert.Equal("0:01.20", _state.GetDisplayedTimeText());
    }

    [Fact]
    public void FrameDisplayText_IsNotPartOfCombatStatusBarState()
    {
        var frameTextMethod = typeof(CombatStatusBarState).GetMethod(
            "GetDisplayedFrameText",
            BindingFlags.Public
                | BindingFlags.NonPublic
                | BindingFlags.Static
                | BindingFlags.Instance
        );

        Assert.Null(frameTextMethod);
    }

    [Fact]
    public void DisplayedTimeText_StillUsesLogicalFrameIndex()
    {
        _state.BeginCombatPlayback();

        _state.AdvanceCombatFrame();
        Assert.Equal("0:00.00", _state.GetDisplayedTimeText());

        _state.AdvanceCombatFrame();
        Assert.Equal("0:00.05", _state.GetDisplayedTimeText());
    }

    [Fact]
    public void CombatSpeed_PlaqueCyclesThroughEverySupportedStepAndWraps()
    {
        Assert.Equal(0.5f, _state.CycleCombatSpeed(), 3);
        Assert.Equal(0.67f, _state.CycleCombatSpeed(), 3);
        Assert.Equal(1f, _state.CycleCombatSpeed(), 3);
        Assert.Equal(0.5f, _state.CycleCombatSpeed(), 3);
    }

    [Fact]
    public void CombatSpeed_UsesRequestedDiscreteSteps()
    {
        Assert.Equal(1f, _state.CombatSpeedMultiplier, 3);
        Assert.Equal(1f, CombatStatusBarState.NormalizeConfiguredDefaultSpeed(1f), 3);
        Assert.Equal(1f, CombatStatusBarState.NormalizeConfiguredDefaultSpeed(1.25f), 3);

        _state.SetCombatSpeed(0.67f);
        Assert.Equal(0.67f, _state.CombatSpeedMultiplier, 3);
    }

    [Fact]
    public void CombatSpeed_OnlyOverridesDuringPlayback()
    {
        Assert.False(_state.ShouldOverrideCombatSpeed(1f));

        _state.SetCombatSpeed(0.5f);

        _state.BeginCombatPlayback();

        Assert.True(_state.ShouldOverrideCombatSpeed(1f));
        Assert.False(_state.ShouldOverrideCombatSpeed(1.2f));
    }

    [Fact]
    public void ShouldRenderForState_RequiresInGameRun()
    {
        _runContext.IsInGameRun = false;
        Assert.False(CombatStatusBarState.ShouldRenderForState(true, _runContext, _probe));

        _runContext.IsInGameRun = true;
        Assert.True(CombatStatusBarState.ShouldRenderForState(true, _runContext, _probe));
    }

    [Fact]
    public void ShouldRenderForState_ShowsDuringReplay_WhenProbeReportsInGameRun()
    {
        // Replay's start guard forces the cached flag to false, and entering ReplayState never
        // fires RunStarted, so the cache stays false throughout playback...
        _runContext.IsInGameRun = false;
        // ...but the live probe reports ReplayState as in-game-run.
        _probe.Result = true;

        Assert.True(CombatStatusBarState.ShouldRenderForState(true, _runContext, _probe));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void DisabledStatusBarStaysHiddenInLiveRunsAndReplays(bool inRun, bool inReplay)
    {
        _runContext.IsInGameRun = inRun;
        _probe.Result = inReplay;

        Assert.False(CombatStatusBarState.ShouldRenderForState(false, _runContext, _probe));
        Assert.True(CombatStatusBarState.ShouldRenderForState(true, _runContext, _probe));
    }

    [Fact]
    public void HidingPausedControlsResumesPlaybackWithoutResettingPreferredSpeed()
    {
        _pause.Paused = false;
        _state.BeginCombatPlayback();
        _state.SetCombatSpeed(0.67f);
        _state.SetCombatPaused(true);

        _state.OnVisibilitySettingChanged(false);

        Assert.False(_pause.Paused);
        Assert.False(_state.IsCombatPaused);
        Assert.True(_state.IsCombatPlaybackActive);
        Assert.Equal(0.67f, _state.CombatSpeedMultiplier);
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
        var result = CombatStatusBarState.AdvanceVisualBlend(current, active, deltaTime);

        Assert.Equal(expected, result, precision: 3);
    }
}
