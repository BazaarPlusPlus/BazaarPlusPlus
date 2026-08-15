#nullable enable
using BazaarPlusPlus.Game.PostCombatImpact.Data;

namespace BazaarPlusPlus.Game.PostCombatImpact.Ui;

internal static class CombatImpactEffectIconKey
{
    internal static string Resolve(CombatImpactKind kind, string nativeAttributeKey)
    {
        if (kind == CombatImpactKind.Destroy)
            return "Destroy";

        if (kind != CombatImpactKind.AttributeChange)
            return nativeAttributeKey;

        var (baseAttributeKey, variant) = SplitAttributeKey(nativeAttributeKey);
        return ResolveAttribute(baseAttributeKey, variant);
    }

    internal static string ResolveAttribute(string key, string? variant) =>
        key switch
        {
            "EnchantTargets" when !string.IsNullOrWhiteSpace(variant) => ResolveEnchantment(
                variant
            ),
            "EnchantTargets" or "EnchantRemoveTargets" => "Enchant",
            "ChargeAmount" or "ChargeTargets" or "PercentChargeReduction" => "Charge",
            "Haste" or "HasteAmount" or "HasteTargets" or "PercentHasteReduction" => "Haste",
            "Slow" or "SlowAmount" or "SlowTargets" or "PercentSlowReduction" => "Slow",
            "Freeze" or "FreezeAmount" or "FreezeTargets" or "PercentFreezeReduction" => "Freeze",
            "RepairTargets" => "Repair",
            "TransformTargets" => "Transform",
            "UpgradeTargets" => "Upgrade",
            "Flying" or "FlyingTargets" => "Flying",
            "Health"
            or "HealthMax"
            or "HealthMaxIncrease"
            or "HealthMaxDecrease"
            or "HealAmount"
            // Return a native typography lookup key, not the sprite name. The Healing
            // sprite is registered under HealAmount; looking up "Healing" silently
            // returns an empty string and leaves the title without an icon.
            or "HealCrit" => "HealAmount",
            "HealthRegen" or "RegenApplyAmount" or "RegenRemoveAmount" or "RegenCrit" => "Regen",
            "Rage" or "RageMax" or "RageApplyAmount" or "RageRemoveAmount" => "Rage",
            "Tempo"
            or "TempoCost"
            or "TempoApplyAmount"
            or "TempoRemoveAmount"
            or "FlatTempoCostReduction"
            or "PercentTempoCostReduction"
            or "TempoGainCooldownMax"
            or "FlatTempoGainCooldownReduction"
            or "PercentTempoGainCooldownReduction" => "Tempo",
            "Burn" or "BurnApplyAmount" or "BurnRemoveAmount" or "BurnCrit" => "Burn",
            "Poison" or "PoisonApplyAmount" or "PoisonRemoveAmount" or "PoisonCrit" => "Poison",
            "Shield" or "ShieldApplyAmount" or "ShieldRemoveAmount" or "ShieldCrit" => "Shield",
            "DamageAmount" => "Damage",
            "DamageCrit" => "CritChance",
            "AmmoMax" => "Ammo",
            "DestroyTargets" or "DestroyImmunity" => "Destroy",
            _ => key,
        };

    private static string ResolveEnchantment(string enchantment) =>
        enchantment switch
        {
            "Deadly" => "CritChance",
            "Fiery" => "Burn",
            "Heavy" => "Slow",
            "Icy" => "Freeze",
            "Mossy" => "Regen",
            "Obsidian" => "Damage",
            "Restorative" => "HealAmount",
            "Shielded" => "Shield",
            "Toxic" => "Poison",
            "Turbo" => "Haste",
            _ => "Enchant",
        };

    internal static (string BaseKey, string? Variant) SplitAttributeKey(string key)
    {
        var separator = key.IndexOf(':');
        return separator < 0 ? (key, null) : (key[..separator], key[(separator + 1)..]);
    }
}
