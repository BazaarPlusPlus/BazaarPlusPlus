using BazaarPlusPlus.Game.PostCombatImpact.Data;
using BazaarPlusPlus.Game.PostCombatImpact.Ui;
using Xunit;

namespace PostCombatImpact.Tests;

public sealed class CombatImpactAttributeLabelTests
{
    [Theory]
    [InlineData(
        "HealthMax",
        (int)CombatImpactEventSurface.AppliedEffect,
        "Max Health",
        "最大生命值"
    )]
    [InlineData(
        "HealthMaxIncrease",
        (int)CombatImpactEventSurface.AppliedEffect,
        "Max Health",
        "最大生命值"
    )]
    [InlineData("DamageAmount", (int)CombatImpactEventSurface.AppliedEffect, "Damage", "伤害")]
    [InlineData("CritChance", (int)CombatImpactEventSurface.AppliedEffect, "Crit Chance", "暴击率")]
    [InlineData("BurnApplyAmount", (int)CombatImpactEventSurface.AppliedEffect, "Burn", "燃烧")]
    [InlineData(
        "TempoApplyAmount",
        (int)CombatImpactEventSurface.AppliedEffect,
        "Tempo Gained",
        "获得节奏"
    )]
    [InlineData(
        "TempoRemoveAmount",
        (int)CombatImpactEventSurface.AppliedEffect,
        "Tempo Spent",
        "消耗节奏"
    )]
    [InlineData("Tempo", (int)CombatImpactEventSurface.PlayerAttribute, "Tempo", "节奏")]
    [InlineData(
        "FlyingTargets",
        (int)CombatImpactEventSurface.AppliedEffect,
        "Flying Targets",
        "起飞目标"
    )]
    [InlineData(
        "DestroyTargets",
        (int)CombatImpactEventSurface.AppliedEffect,
        "Destroy Targets",
        "摧毁目标"
    )]
    [InlineData(
        "ForceUseTargets",
        (int)CombatImpactEventSurface.AppliedEffect,
        "Force Use",
        "强制使用"
    )]
    [InlineData("EnchantTargets", (int)CombatImpactEventSurface.AppliedEffect, "Enchant", "附魔")]
    [InlineData(
        "EnchantTargets:Fiery",
        (int)CombatImpactEventSurface.AppliedEffect,
        "Enchant",
        "附魔"
    )]
    [InlineData(
        "EnchantRemoveTargets",
        (int)CombatImpactEventSurface.AppliedEffect,
        "Enchant Removed",
        "移除附魔"
    )]
    [InlineData(
        "TransformTargets",
        (int)CombatImpactEventSurface.AppliedEffect,
        "Transform",
        "变形"
    )]
    [InlineData("UpgradeTargets", (int)CombatImpactEventSurface.AppliedEffect, "Upgrade", "升级")]
    [InlineData("RepairTargets", (int)CombatImpactEventSurface.AppliedEffect, "Repair", "修复")]
    [InlineData(
        "PercentDamageReduction",
        (int)CombatImpactEventSurface.AppliedEffect,
        "Damage Reduction",
        "伤害减免"
    )]
    [InlineData(
        "RerollCostModifier",
        (int)CombatImpactEventSurface.AppliedEffect,
        "Reroll Cost",
        "刷新费用"
    )]
    [InlineData(
        "DamageAmount",
        (int)CombatImpactEventSurface.CardAttribute,
        "Damage Gain",
        "伤害增加"
    )]
    [InlineData(
        "RegenApplyAmount",
        (int)CombatImpactEventSurface.CardAttribute,
        "Regen Gain",
        "再生增加"
    )]
    [InlineData(
        "FutureAttributeValue",
        (int)CombatImpactEventSurface.AppliedEffect,
        "Attribute Change",
        "属性变化"
    )]
    public void Internal_keys_use_localized_player_facing_labels(
        string key,
        int surfaceValue,
        string english,
        string chinese
    )
    {
        var surface = (CombatImpactEventSurface)surfaceValue;

        Assert.Equal(
            english,
            CombatImpactAttributeLabel.Resolve(key, surface, changeValue: 1, chinese: false)
        );
        Assert.Equal(
            chinese,
            CombatImpactAttributeLabel.Resolve(key, surface, changeValue: 1, chinese: true)
        );
    }

    [Theory]
    [InlineData("DamageAmount", -5, "Damage Loss", "伤害减少")]
    [InlineData("BurnRemoveAmount", 5, "Burn Removed", "移除燃烧")]
    [InlineData("ShieldRemoveAmount", -5, "Shield Removed", "移除护盾")]
    [InlineData("TempoApplyAmount", 5, "Tempo Gain", "节奏获取量")]
    [InlineData("TempoRemoveAmount", -5, "Tempo Removal", "节奏移除量")]
    public void Card_attribute_labels_distinguish_direction_without_redundant_removal_gain(
        string key,
        int changeValue,
        string english,
        string chinese
    )
    {
        Assert.Equal(
            english,
            CombatImpactAttributeLabel.Resolve(
                key,
                CombatImpactEventSurface.CardAttribute,
                changeValue,
                chinese: false
            )
        );
        Assert.Equal(
            chinese,
            CombatImpactAttributeLabel.Resolve(
                key,
                CombatImpactEventSurface.CardAttribute,
                changeValue,
                chinese: true
            )
        );
    }

    [Fact]
    public void Mixed_card_attribute_directions_use_a_neutral_change_label()
    {
        Assert.Equal(
            "Shield Change",
            CombatImpactAttributeLabel.Resolve(
                "ShieldApplyAmount",
                CombatImpactEventSurface.CardAttribute,
                changeValue: -82,
                chinese: false,
                hasMixedValueDirections: true
            )
        );
        Assert.Equal(
            "护盾变化",
            CombatImpactAttributeLabel.Resolve(
                "ShieldApplyAmount",
                CombatImpactEventSurface.CardAttribute,
                changeValue: -82,
                chinese: true,
                hasMixedValueDirections: true
            )
        );
    }
}
