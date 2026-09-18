using BazaarPlusPlus.Game.PostCombatImpact.Data;
using BazaarPlusPlus.Game.PostCombatImpact.Ui;
using Xunit;

namespace PostCombatImpact.Tests;

public sealed class CombatImpactTargetDetailPolicyTests
{
    [Theory]
    [InlineData(
        (int)CombatImpactKind.AttributeChange,
        "RegenApplyAmount",
        (int)CombatImpactEventSurface.AppliedEffect,
        false
    )]
    [InlineData(
        (int)CombatImpactKind.AttributeChange,
        "RegenApplyAmount",
        (int)CombatImpactEventSurface.CardAttribute,
        true
    )]
    [InlineData(
        (int)CombatImpactKind.AttributeChange,
        "TempoApplyAmount",
        (int)CombatImpactEventSurface.AppliedEffect,
        false
    )]
    [InlineData(
        (int)CombatImpactKind.AttributeChange,
        "TempoRemoveAmount",
        (int)CombatImpactEventSurface.AppliedEffect,
        false
    )]
    [InlineData(
        (int)CombatImpactKind.AttributeChange,
        "BurnRemoveAmount",
        (int)CombatImpactEventSurface.AppliedEffect,
        false
    )]
    [InlineData(
        (int)CombatImpactKind.AttributeChange,
        "ForceUseTargets",
        (int)CombatImpactEventSurface.AppliedEffect,
        true
    )]
    [InlineData(
        (int)CombatImpactKind.AttributeChange,
        "TempoApplyAmount",
        (int)CombatImpactEventSurface.CardAttribute,
        true
    )]
    [InlineData(
        (int)CombatImpactKind.DirectDamage,
        "DamageAmount",
        (int)CombatImpactEventSurface.AppliedEffect,
        false
    )]
    public void Target_rows_follow_effect_surface_policy(
        int kindValue,
        string nativeKey,
        int surfaceValue,
        bool expected
    ) =>
        Assert.Equal(
            expected,
            CombatImpactTargetDetailPolicy.ShouldRender(
                (CombatImpactKind)kindValue,
                nativeKey,
                (CombatImpactEventSurface)surfaceValue
            )
        );
}
