#nullable enable
using BazaarPlusPlus.Game.PostCombatImpact.Data;

namespace BazaarPlusPlus.Game.PostCombatImpact.Ui;

internal static class CombatImpactAttributeLabel
{
    internal static string Resolve(
        string key,
        CombatImpactEventSurface surface,
        int? changeValue,
        bool chinese,
        bool hasMixedValueDirections = false
    )
    {
        var variantSeparator = key.IndexOf(':');
        if (variantSeparator >= 0)
            key = key[..variantSeparator];
        var normalized =
            key.EndsWith("Increase", StringComparison.Ordinal) ? key[..^"Increase".Length]
            : key.EndsWith("Decrease", StringComparison.Ordinal) ? key[..^"Decrease".Length]
            : key;

        var labels = normalized switch
        {
            "AmmoMax" => ("最大弹药", "Max Ammo"),
            "ReloadAmount" => ("装填", "Reload"),
            "ReloadTargets" => ("装填目标", "Reload Targets"),
            "CooldownMax" => ("冷却时间", "Cooldown"),
            "ChargeAmount" => ("充能", "Charge"),
            "ChargeTargets" => ("充能目标", "Charge Targets"),
            "HasteAmount" => ("加速", "Haste"),
            "HasteTargets" => ("加速目标", "Haste Targets"),
            "SlowAmount" => ("减速", "Slow"),
            "SlowTargets" => ("减速目标", "Slow Targets"),
            "FreezeAmount" => ("冻结", "Freeze"),
            "FreezeTargets" => ("冻结目标", "Freeze Targets"),
            "BurnApplyAmount" => ("燃烧", "Burn"),
            "BurnRemoveAmount" => ("移除燃烧", "Burn Removed"),
            "PoisonApplyAmount" => ("中毒", "Poison"),
            "PoisonRemoveAmount" => ("移除中毒", "Poison Removed"),
            "RegenApplyAmount" or "HealthRegen" => ("再生", "Regen"),
            "RegenRemoveAmount" => ("移除再生", "Regen Removed"),
            "RageApplyAmount" or "Rage" => ("怒气", "Rage"),
            "RageRemoveAmount" => ("移除怒气", "Rage Removed"),
            "RageMax" => ("最大怒气", "Max Rage"),
            "Tempo" => ("节奏", "Tempo"),
            "TempoApplyAmount" => surface == CombatImpactEventSurface.AppliedEffect
                ? ("获得节奏", "Tempo Gained")
                : ("节奏获取量", "Tempo Gain"),
            "TempoRemoveAmount" => surface == CombatImpactEventSurface.AppliedEffect
                ? ("消耗节奏", "Tempo Spent")
                : ("节奏移除量", "Tempo Removal"),
            "Multicast" => ("多重施放", "Multicast"),
            "Lifesteal" => ("吸血", "Lifesteal"),
            "CritChance" => ("暴击率", "Crit Chance"),
            "DamageAmount" => ("伤害", "Damage"),
            "DamageCrit" => ("伤害暴击", "Damage Crit"),
            "HealAmount" => ("治疗", "Heal"),
            "HealCrit" => ("治疗暴击", "Heal Crit"),
            "JoyApplyAmount" => ("快乐", "Joy"),
            "JoyRemoveAmount" => ("快乐移除量", "Joy Removal"),
            "JoyCrit" => ("快乐暴击", "Joy Crit"),
            "ShieldApplyAmount" => ("护盾", "Shield"),
            "ShieldRemoveAmount" => ("移除护盾", "Shield Removed"),
            "ShieldCrit" => ("护盾暴击", "Shield Crit"),
            "ForceUseTargets" => ("强制使用", "Force Use"),
            "EnchantTargets" => ("附魔", "Enchant"),
            "UpgradeTargets" => ("升级", "Upgrade"),
            "DisableTargets" => ("禁用目标", "Disable Targets"),
            "RepairTargets" => ("修复", "Repair"),
            "BurnCrit" => ("燃烧暴击", "Burn Crit"),
            "PoisonCrit" => ("中毒暴击", "Poison Crit"),
            "DestroyTargets" => ("摧毁目标", "Destroy Targets"),
            "RegenCrit" => ("再生暴击", "Regen Crit"),
            "TransformTargets" => ("变形", "Transform"),
            "HealthMax" => ("最大生命值", "Max Health"),
            "FlatCooldownReduction" => ("固定冷却缩减", "Flat Cooldown Reduction"),
            "PercentCooldownReduction" => ("冷却缩减", "Cooldown Reduction"),
            "EnchantRemoveTargets" => ("移除附魔", "Enchant Removed"),
            "FlyingTargets" => ("起飞目标", "Flying Targets"),
            "PercentChargeReduction" => ("充能缩减", "Charge Reduction"),
            "PercentHasteReduction" => ("加速缩减", "Haste Reduction"),
            "PercentSlowReduction" => ("减速抗性", "Slow Resistance"),
            "PercentFreezeReduction" => ("冻结抗性", "Freeze Resistance"),
            "DestroyImmunity" => ("摧毁免疫", "Destroy Immunity"),
            "BuyPrice" => ("购买价格", "Buy Price"),
            "SellPrice" => ("出售价格", "Sell Price"),
            "Experience" => ("经验", "Experience"),
            "Gold" => ("金币", "Gold"),
            "Income" => ("收入", "Income"),
            "Prestige" => ("声望", "Prestige"),
            "Level" => ("等级", "Level"),
            "RerollCostModifier" => ("刷新费用", "Reroll Cost"),
            "FlatDamageReduction" => ("固定伤害减免", "Flat Damage Reduction"),
            "PercentDamageReduction" => ("伤害减免", "Damage Reduction"),
            "EnragedDurationMax" => ("最大激怒时长", "Max Enraged Duration"),
            "TempoGainCooldownMax" => ("节奏获取冷却", "Tempo Gain Cooldown"),
            "FlatTempoGainCooldownReduction" => (
                "固定节奏获取冷却缩减",
                "Flat Tempo Gain Cooldown Reduction"
            ),
            "PercentTempoGainCooldownReduction" => (
                "节奏获取冷却缩减",
                "Tempo Gain Cooldown Reduction"
            ),
            "TempoCost" => ("节奏消耗", "Tempo Cost"),
            "FlatTempoCostReduction" => ("固定节奏消耗缩减", "Flat Tempo Cost Reduction"),
            "PercentTempoCostReduction" => ("节奏消耗缩减", "Tempo Cost Reduction"),
            _ => ("属性变化", "Attribute Change"),
        };
        if (surface == CombatImpactEventSurface.CardAttribute && UsesDirectionalWording(normalized))
        {
            if (hasMixedValueDirections)
                return chinese ? $"{labels.Item1}变化" : $"{labels.Item2} Change";
            if (changeValue > 0)
                return chinese ? $"{labels.Item1}增加" : $"{labels.Item2} Gain";
            if (changeValue < 0)
                return chinese ? $"{labels.Item1}减少" : $"{labels.Item2} Loss";
        }

        return chinese ? labels.Item1 : labels.Item2;
    }

    private static bool UsesDirectionalWording(string key) =>
        key
            is "AmmoMax"
                or "ReloadAmount"
                or "ChargeAmount"
                or "HasteAmount"
                or "SlowAmount"
                or "FreezeAmount"
                or "BurnApplyAmount"
                or "PoisonApplyAmount"
                or "RegenApplyAmount"
                or "RageApplyAmount"
                or "Multicast"
                or "Lifesteal"
                or "CritChance"
                or "DamageAmount"
                or "HealAmount"
                or "JoyApplyAmount"
                or "ShieldApplyAmount";
}
