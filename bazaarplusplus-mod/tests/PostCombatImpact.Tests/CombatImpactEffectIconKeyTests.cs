using BazaarPlusPlus.Game.PostCombatImpact.Data;
using BazaarPlusPlus.Game.PostCombatImpact.Ui;
using Xunit;

namespace PostCombatImpact.Tests;

public sealed class CombatImpactEffectIconKeyTests
{
    [Theory]
    [InlineData("ChargeAmount", "Charge")]
    [InlineData("ChargeTargets", "Charge")]
    [InlineData("PercentChargeReduction", "Charge")]
    [InlineData("HasteAmount", "Haste")]
    [InlineData("HasteTargets", "Haste")]
    [InlineData("PercentHasteReduction", "Haste")]
    [InlineData("SlowAmount", "Slow")]
    [InlineData("SlowTargets", "Slow")]
    [InlineData("PercentSlowReduction", "Slow")]
    [InlineData("FreezeAmount", "Freeze")]
    [InlineData("FreezeTargets", "Freeze")]
    [InlineData("PercentFreezeReduction", "Freeze")]
    [InlineData("RepairTargets", "Repair")]
    [InlineData("TransformTargets", "Transform")]
    [InlineData("UpgradeTargets", "Upgrade")]
    [InlineData("FlyingTargets", "Flying")]
    [InlineData("EnchantTargets", "Enchant")]
    [InlineData("EnchantRemoveTargets", "Enchant")]
    [InlineData("DestroyImmunity", "Destroy")]
    [InlineData("DamageAmount", "Damage")]
    [InlineData("BurnRemoveAmount", "Burn")]
    [InlineData("PoisonRemoveAmount", "Poison")]
    [InlineData("RegenRemoveAmount", "Regen")]
    [InlineData("ShieldRemoveAmount", "Shield")]
    [InlineData("Health", "HealAmount")]
    [InlineData("HealthMax", "HealAmount")]
    [InlineData("HealthMaxIncrease", "HealAmount")]
    [InlineData("HealthMaxDecrease", "HealAmount")]
    [InlineData("HealAmount", "HealAmount")]
    [InlineData("HealCrit", "HealAmount")]
    [InlineData("RageRemoveAmount", "Rage")]
    [InlineData("TempoCost", "Tempo")]
    [InlineData("FlatTempoCostReduction", "Tempo")]
    [InlineData("PercentTempoCostReduction", "Tempo")]
    [InlineData("TempoGainCooldownMax", "Tempo")]
    public void Attribute_keys_use_native_keyword_icon_keys(string attributeKey, string iconKey)
    {
        Assert.Equal(iconKey, CombatImpactEffectIconKey.ResolveAttribute(attributeKey, null));
    }

    [Theory]
    [InlineData("Deadly", "CritChance")]
    [InlineData("Fiery", "Burn")]
    [InlineData("Heavy", "Slow")]
    [InlineData("Icy", "Freeze")]
    [InlineData("Mossy", "Regen")]
    [InlineData("Obsidian", "Damage")]
    [InlineData("Restorative", "HealAmount")]
    [InlineData("Shielded", "Shield")]
    [InlineData("Toxic", "Poison")]
    [InlineData("Turbo", "Haste")]
    public void Combat_enchant_variants_use_their_effect_icon(string enchantment, string iconKey)
    {
        Assert.Equal(
            iconKey,
            CombatImpactEffectIconKey.Resolve(
                CombatImpactKind.AttributeChange,
                $"EnchantTargets:{enchantment}"
            )
        );
    }

    [Theory]
    [InlineData("Golden")]
    [InlineData("Radiant")]
    [InlineData("Shiny")]
    [InlineData("Unknown")]
    public void Enchant_variants_without_one_effect_use_the_generic_icon(string enchantment)
    {
        Assert.Equal(
            "Enchant",
            CombatImpactEffectIconKey.Resolve(
                CombatImpactKind.AttributeChange,
                $"EnchantTargets:{enchantment}"
            )
        );
    }

    [Fact]
    public void Destroy_kind_uses_destroy_icon_key()
    {
        Assert.Equal(
            "Destroy",
            CombatImpactEffectIconKey.Resolve(CombatImpactKind.Destroy, "DestroyTargets")
        );
    }
}
