using BazaarGameShared.Domain.Core.Types;
using BazaarGameShared.Domain.Effect;
using BazaarGameShared.Domain.Effect.Actions;
using BazaarGameShared.Domain.Effect.Trigger;
using BazaarPlusPlus.Game.PostCombatImpact.Data;
using Xunit;

namespace PostCombatImpact.Tests;

public sealed class CombatImpactCriticalCapabilityTests
{
    [Fact]
    public void Fired_burn_effect_is_crit_capable()
    {
        var effectIds = CombatImpactCriticalCapability.ReadCritCapableEffectIds(
            ECardType.Item,
            [Ability("burn", new TTriggerOnCardFired())],
            []
        );

        Assert.Equal(["burn"], effectIds);
    }

    [Fact]
    public void Burn_triggered_by_another_item_is_not_crit_capable()
    {
        var effectIds = CombatImpactCriticalCapability.ReadCritCapableEffectIds(
            ECardType.Item,
            [Ability("passive-burn", new TTriggerOnItemUsed())],
            []
        );

        Assert.Null(effectIds);
    }

    [Fact]
    public void Native_can_crit_tag_overrides_the_trigger_shape()
    {
        var effectIds = CombatImpactCriticalCapability.ReadCritCapableEffectIds(
            ECardType.Item,
            [Ability("passive-burn", new TTriggerOnItemUsed())],
            [EHiddenTag.CanCrit]
        );

        Assert.Equal(["passive-burn"], effectIds);
    }

    [Fact]
    public void Skill_without_native_override_is_not_crit_capable()
    {
        var effectIds = CombatImpactCriticalCapability.ReadCritCapableEffectIds(
            ECardType.Skill,
            [Ability("burn", new TTriggerOnCardFired())],
            []
        );

        Assert.Null(effectIds);
    }

    private static TCardAbility Ability(string id, TTriggerBase trigger) =>
        new()
        {
            Id = id,
            Trigger = trigger,
            Action = new TActionPlayerBurnApply(),
        };
}
