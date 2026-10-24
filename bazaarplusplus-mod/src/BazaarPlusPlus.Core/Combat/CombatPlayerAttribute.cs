#nullable enable
namespace BazaarPlusPlus.Core.Combat;

/// <summary>
/// A player attribute a combat frame changes.
/// A combat facet (ADR-0019): members mirror the game's enum names so host adapters map by
/// name. The values copy the game's only so set and sort order match the game's; nothing casts
/// between the two, and no value is persisted or sent.
/// </summary>
public enum CombatPlayerAttribute
{
    Burn = 0,
    CritChance = 1,
    DamageCrit = 2,
    Experience = 3,
    Gold = 4,
    Income = 5,
    Joy = 6,
    JoyCrit = 8,
    Prestige = 9,
    Health = 10,
    HealthMax = 11,
    HealthRegen = 12,
    HealAmount = 13,
    HealCrit = 14,
    Level = 15,
    Poison = 16,
    RerollCostModifier = 17,
    Shield = 19,
    ShieldCrit = 21,
    FlatDamageReduction = 22,
    PercentDamageReduction = 23,
    Custom_0 = 24,
    Custom_1 = 25,
    Custom_2 = 26,
    Custom_3 = 27,
    Custom_4 = 28,
    Custom_5 = 29,
    Custom_6 = 30,
    Custom_7 = 31,
    Custom_8 = 32,
    Custom_9 = 33,
    Rage = 34,
    RageMax = 35,
    Enraged = 36,
    EnragedDuration = 37,
    EnragedDurationMax = 38,
    Tempo = 39,
    TempoGainCooldownMax = 40,
    FlatTempoGainCooldownReduction = 41,
    PercentTempoGainCooldownReduction = 42,
    Custom_10 = 43,
    Custom_11 = 44,
    Custom_12 = 45,
    Custom_13 = 46,
    Custom_14 = 47,
    Custom_15 = 48,
    Custom_16 = 49,
    Custom_17 = 50,
    Custom_18 = 51,
    Custom_19 = 52,
}
